using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Microsoft.AspNetCore.Identity;
using Services.Messaging;
using Shared.Messaging;

namespace Services.EventHandlers
{
    public class PasswordResetRequestedHandler(
        UserManager<ApplicationUser> userManager, IUnitOfWork uow, ITemplatedMessenger messenger, FrontendLinks links, IClock clock,
        ILogger<PasswordResetRequestedHandler> logger) : IDomainEventHandler<PasswordResetRequested>
    {
        public async Task HandleAsync(PasswordResetRequested domainEvent, long outboxMessageId, CancellationToken ct)
        {
            // Checked again: the account may have been suspended since the request
            var user = await userManager.FindByIdAsync(domainEvent.UserId.ToString());
            if (user is null || !user.IsActive || string.IsNullOrEmpty(user.Email))
            {
                logger.LogInformation("Password reset email for user {UserId} skipped: no such user, no email, or inactive", domainEvent.UserId);
                return;
            }

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.Email, async () =>
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var url = links.ResetPassword(user.Id, Uri.EscapeDataString(token));
                await messenger.SendEmailAsync(
                    user, TemplateKeys.PasswordReset, MessageValues.Account(user, TemplatePlaceholders.ResetUrl, url), ct);
            }, ct);
        }
    }
}
