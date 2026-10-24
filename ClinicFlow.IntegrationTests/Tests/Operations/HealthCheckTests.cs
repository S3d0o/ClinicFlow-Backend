using ClinicFlow.HealthChecks;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.Messaging;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.Operations
{
    // The probes (docs/v2/06 §14, REVIEW.md M12): live answers while the process does; ready reports the database
    // and the outbox lag
    [Collection(nameof(IntegrationTestCollection))]
    public class HealthCheckTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private readonly HttpClient _client = factory.CreateClient();

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Theory]
        [InlineData("/health/live")]
        [InlineData("/health")] // V1's route, kept as an alias
        public async Task Live_ReturnsHealthy(string url)
        {
            var response = await _client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
        }

        [Fact]
        public async Task Ready_WithTheDatabaseUpAndNothingWaiting_ReportsEachCheckHealthy()
        {
            var body = await ReadyAsync();

            body["status"]!.GetValue<string>().Should().Be("Healthy");
            body["checks"]!["database"]!["status"]!.GetValue<string>().Should().Be("Healthy");
            body["checks"]!["outbox"]!["status"]!.GetValue<string>().Should().Be("Healthy");
            body["checks"]!["outbox"]!["lagSeconds"]!.GetValue<int>().Should().Be(0);
        }

        [Fact]
        public async Task Ready_WhenADueMessageHasWaitedTooLong_IsDegradedButStillServes()
        {
            await AddMessageAsync(occurredAgo: TimeSpan.FromMinutes(10));

            var body = await ReadyAsync(); // Degraded is 200: the instance stays in rotation

            body["status"]!.GetValue<string>().Should().Be("Degraded");
            var outbox = body["checks"]!["outbox"]!;
            outbox["status"]!.GetValue<string>().Should().Be("Degraded");
            outbox["lagSeconds"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(590);
            outbox["description"]!.GetValue<string>().Should().Contain("waited");
        }

        [Fact]
        public async Task Ready_MessagesBackingOffOrDeadLettered_AreNotLag()
        {
            await AddMessageAsync(occurredAgo: TimeSpan.FromMinutes(10), nextAttemptIn: TimeSpan.FromMinutes(5));
            await AddMessageAsync(occurredAgo: TimeSpan.FromHours(2), deadLettered: true);
            await AddMessageAsync(occurredAgo: TimeSpan.FromSeconds(5)); // fresh: well under the limit

            var outbox = (await ReadyAsync())["checks"]!["outbox"]!;

            outbox["status"]!.GetValue<string>().Should().Be("Healthy");
            outbox["lagSeconds"]!.GetValue<int>().Should().BeLessThan((int)OutboxHealthCheck.MaxLag.TotalSeconds);
            outbox["deadLettered"]!.GetValue<int>().Should().Be(1);
        }

        private async Task<JsonNode> ReadyAsync()
        {
            var response = await _client.GetAsync("/health/ready");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
            return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        }

        private Task AddMessageAsync(TimeSpan occurredAgo, TimeSpan? nextAttemptIn = null, bool deadLettered = false)
            => factory.SeedAsync(async db =>
            {
                var now = DateTime.UtcNow;
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Type = "Test.Event",
                    Payload = "{}",
                    OccurredAtUtc = now - occurredAgo,
                    NextAttemptAtUtc = now + nextAttemptIn,
                    Attempts = nextAttemptIn is null ? 0 : 1,
                    DeadLetteredAtUtc = deadLettered ? now : null,
                });
                await db.SaveChangesAsync();
            });
    }
}
