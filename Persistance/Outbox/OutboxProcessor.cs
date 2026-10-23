using System.Reflection;
using Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.Abstraction.Contracts;

namespace Persistence.Outbox
{
    // Delivers outbox messages (docs/v2/06 §5): claims a batch of due messages, runs every handler of each in its own
    // scope, then marks it processed, or schedules a retry with exponential backoff, or dead-letters it after
    // MaxAttempts. The OutboxDispatcher job calls it every 2 s; integration tests call it through DrainOutboxAsync.
    // It reads every organization's messages (a platform job); handlers run with the app's tenant, which is the one
    // organization until V3
    public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, IClock clock, ILogger<OutboxProcessor> logger)
    {
        public const int BatchSize = 50;
        public const int MaxAttempts = 10;

        // A claim expires after this, so a dispatcher that dies mid-batch doesn't hold its messages forever
        private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);

        public Task<int> ProcessBatchAsync(CancellationToken ct) => ProcessBatchAsync(clock.UtcNow, ct);

        // Returns how many messages it claimed. utcNow is a parameter so tests can step past the backoff
        public async Task<int> ProcessBatchAsync(DateTime utcNow, CancellationToken ct)
        {
            var claimed = await ClaimAsync(utcNow, ct);
            foreach (var message in claimed)
                await DeliverAsync(message, utcNow, ct);
            return claimed.Count;
        }

        // 2 s, 4 s, 8 s, … capped at an hour: about 34 minutes over the ten attempts
        public static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, attempts)));

        // UPDLOCK + READPAST: two dispatchers running at once skip each other's rows instead of both taking them
        private async Task<List<ClaimedMessage>> ClaimAsync(DateTime utcNow, CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            var lockedUntil = utcNow + ClaimDuration;

            return await db.Database.SqlQuery<ClaimedMessage>($"""
                WITH batch AS (
                    SELECT TOP ({BatchSize}) Id, Type, Payload, Attempts, LockedUntilUtc
                    FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NULL
                      AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= {utcNow})
                      AND (LockedUntilUtc IS NULL OR LockedUntilUtc < {utcNow})
                    ORDER BY NextAttemptAtUtc, Id)
                UPDATE batch SET LockedUntilUtc = {lockedUntil}
                OUTPUT inserted.Id, inserted.Type, inserted.Payload, inserted.Attempts
                """).ToListAsync(ct);
        }

        private async Task DeliverAsync(ClaimedMessage message, DateTime utcNow, CancellationToken ct)
        {
            Exception? failure = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var domainEvent = OutboxSerializer.FromMessage(message.Type, message.Payload);
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
                var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

                foreach (var handler in scope.ServiceProvider.GetServices(handlerType))
                    await (Task)handle.Invoke(handler, [domainEvent, message.Id, ct])!;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failure = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            }

            var attempts = message.Attempts + 1;
            using var updateScope = scopeFactory.CreateScope();
            var messages = updateScope.ServiceProvider.GetRequiredService<ClinicDbContext>()
                .OutboxMessages.IgnoreQueryFilters().Where(m => m.Id == message.Id);

            if (failure is null)
            {
                await messages.ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.ProcessedAtUtc, utcNow)
                    .SetProperty(m => m.Attempts, attempts)
                    .SetProperty(m => m.LockedUntilUtc, (DateTime?)null), CancellationToken.None);
                return;
            }

            var error = Truncate(failure.ToString(), 2000);
            if (attempts >= MaxAttempts)
            {
                logger.LogError(failure, "Outbox message {MessageId} ({Type}) failed {Attempts} times and was dead-lettered", message.Id, message.Type, attempts);
                await messages.ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.DeadLetteredAtUtc, utcNow)
                    .SetProperty(m => m.Attempts, attempts)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.LockedUntilUtc, (DateTime?)null), CancellationToken.None);
                return;
            }

            var nextAttempt = utcNow + Backoff(attempts);
            logger.LogWarning(failure, "Outbox message {MessageId} ({Type}) failed on attempt {Attempts}; retrying at {NextAttemptAt}", message.Id, message.Type, attempts, nextAttempt);
            await messages.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.NextAttemptAtUtc, nextAttempt)
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.LockedUntilUtc, (DateTime?)null), CancellationToken.None);
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

        private sealed class ClaimedMessage
        {
            public long Id { get; set; }
            public string Type { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public int Attempts { get; set; }
        }
    }
}
