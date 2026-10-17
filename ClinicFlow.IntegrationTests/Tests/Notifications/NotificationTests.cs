using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Notification;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Notifications
{
    [Collection(nameof(IntegrationTestCollection))]
    public class NotificationTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/notifications";

        private Guid _userId;
        private Guid _otherUserId;
        private int _oldReadId;
        private int _middleUnreadId;
        private int _newestUnreadId;
        private int _otherUsersId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly JsonSerializerOptions _json;

        public NotificationTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _json.Converters.Add(new JsonStringEnumConverter());
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                _userId = await SeedDataHelper.SeedPatientOnlyAsync(db, "inbox");
                _otherUserId = await SeedDataHelper.SeedPatientOnlyAsync(db, "other");

                var now = DateTime.UtcNow;
                var oldRead = Notify(_userId, "Old", isRead: true, now.AddDays(-3));
                var middleUnread = Notify(_userId, "Middle", isRead: false, now.AddDays(-2));
                var newestUnread = Notify(_userId, "Newest", isRead: false, now.AddDays(-1));
                var othersUnread = Notify(_otherUserId, "Someone else's", isRead: false, now);
                db.Notifications.AddRange(oldRead, middleUnread, newestUnread, othersUnread);
                await db.SaveChangesAsync();

                (_oldReadId, _middleUnreadId, _newestUnreadId, _otherUsersId) =
                    (oldRead.Id, middleUnread.Id, newestUnread.Id, othersUnread.Id);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GetMyNotifications_ReturnsOnlyTheCallersNotificationsNewestFirst()
        {
            AuthenticateAs(_userId);

            var response = await _client.GetAsync($"{BaseUrl}/my");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<List<NotificationResponse>>(_json);
            body!.Select(n => n.Id).Should().Equal(_newestUnreadId, _middleUnreadId, _oldReadId);
            body.Should().OnlyContain(n => n.Type == NotificationType.AppointmentConfirmed);
        }

        [Fact]
        public async Task GetMyNotifications_UnreadOnly_LeavesOutReadNotifications()
        {
            AuthenticateAs(_userId);

            var body = await _client.GetFromJsonAsync<List<NotificationResponse>>($"{BaseUrl}/my?unreadOnly=true", _json);

            body!.Select(n => n.Id).Should().Equal(_newestUnreadId, _middleUnreadId);
        }

        [Fact]
        public async Task MarkAsRead_OwnNotification_Returns204AndMarksOnlyThatOne()
        {
            AuthenticateAs(_userId);

            var response = await _client.PostAsync($"{BaseUrl}/{_middleUnreadId}/read", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await ReadStatesAsync()).Should().BeEquivalentTo(new Dictionary<int, bool>
            {
                [_oldReadId] = true, [_middleUnreadId] = true, [_newestUnreadId] = false, [_otherUsersId] = false
            });
        }

        [Fact]
        public async Task MarkAsRead_AnotherUsersNotification_Returns403AndLeavesItUnread()
        {
            AuthenticateAs(_userId);

            var response = await _client.PostAsync($"{BaseUrl}/{_otherUsersId}/read", null);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Notification.Unauthorized");
            (await ReadStatesAsync())[_otherUsersId].Should().BeFalse();
        }

        [Fact]
        public async Task MarkAsRead_UnknownNotification_Returns404()
        {
            AuthenticateAs(_userId);

            var response = await _client.PostAsync($"{BaseUrl}/999999/read", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Notification.NotFound");
        }

        [Fact]
        public async Task MarkAllAsRead_MarksTheCallersNotificationsAndNoOneElses()
        {
            AuthenticateAs(_userId);

            var response = await _client.PostAsync($"{BaseUrl}/read-all", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await ReadStatesAsync()).Should().BeEquivalentTo(new Dictionary<int, bool>
            {
                [_oldReadId] = true, [_middleUnreadId] = true, [_newestUnreadId] = true, [_otherUsersId] = false
            });
        }

        [Theory]
        [InlineData("GET", "/my")]
        [InlineData("POST", "/1/read")]
        [InlineData("POST", "/read-all")]
        public async Task NotificationEndpoints_Anonymous_Return401(string method, string path)
        {
            var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), BaseUrl + path));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ----------------------------------------------------------------

        private static Notification Notify(Guid userId, string title, bool isRead, DateTime createdAt) => new()
        {
            UserId = userId,
            Title = title,
            Message = $"{title} message",
            Type = NotificationType.AppointmentConfirmed,
            IsRead = isRead,
            CreatedAt = createdAt
        };

        private async Task<Dictionary<int, bool>> ReadStatesAsync()
        {
            var states = new Dictionary<int, bool>();
            await _factory.SeedAsync(async db =>
            {
                foreach (var n in await db.Notifications.AsNoTracking().ToListAsync())
                    states[n.Id] = n.IsRead;
            });
            return states;
        }

        private void AuthenticateAs(Guid userId)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");
        }
    }
}
