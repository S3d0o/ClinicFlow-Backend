using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Microsoft.AspNetCore.Identity;

namespace Services.EventHandlers
{
    public class PasswordResetRequestedHandler(
        UserManager<ApplicationUser> userManager, IUnitOfWork uow, IEmailService emailService, IClock clock,
        ILogger<PasswordResetRequestedHandler> logger) : IDomainEventHandler<PasswordResetRequested>
    {
        public async Task HandleAsync(PasswordResetRequested domainEvent, long outboxMessageId, CancellationToken ct)
        {
            // Checked again: the account may have been suspended since the request
            var user = await userManager.FindByIdAsync(domainEvent.UserId.ToString());
            if (user is null || !user.IsActive)
            {
                logger.LogInformation("Password reset email for user {UserId} skipped: no such user, or inactive", domainEvent.UserId);
                return;
            }

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.Email, async () =>
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                await emailService.SendPasswordResetAsync(user.Email!, user.Id, Uri.EscapeDataString(token), ct);
            }, ct);
        }
    }
}
