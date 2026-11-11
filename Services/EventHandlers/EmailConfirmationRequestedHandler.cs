using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Microsoft.AspNetCore.Identity;
using Services.Messaging;
using Shared.Messaging;

namespace Services.EventHandlers
{
    // The token is created when the email is sent, so no token is stored in the outbox. Each retry creates a new
    // one; that is harmless, because the channel is skipped once an email has gone out
    public class EmailConfirmationRequestedHandler(
        UserManager<ApplicationUser> userManager, IUnitOfWork uow, ITemplatedMessenger messenger, FrontendLinks links, IClock clock,
        ILogger<EmailConfirmationRequestedHandler> logger) : IDomainEventHandler<EmailConfirmationRequested>
    {
        public async Task HandleAsync(EmailConfirmationRequested domainEvent, long outboxMessageId, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(domainEvent.UserId.ToString());
            if (user is null || user.EmailConfirmed || string.IsNullOrEmpty(user.Email))
            {
                logger.LogInformation("Confirmation email for user {UserId} skipped: no such user, no email, or already confirmed", domainEvent.UserId);
                return;
            }

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.Email, async () =>
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var url = links.ConfirmEmail(user.Id, Uri.EscapeDataString(token));
                await messenger.SendEmailAsync(
                    user, TemplateKeys.EmailConfirmation, MessageValues.Account(user, TemplatePlaceholders.ConfirmUrl, url), ct);
            }, ct);
        }
    }
}
