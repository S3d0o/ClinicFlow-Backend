using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;
using Services.Messaging;
using Shared.Settings;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    // The confirmation and reset emails: the token is created when the email is sent, and is URL-escaped for the link
    public abstract class AccountEmailHandlerTestsBase : EventHandlerTestsBase
    {
        protected const string RawToken = "token+with/=";
        protected const string FrontendUrl = "https://app.clinicflow.test";
        protected readonly UserManager<ApplicationUser> _userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        protected readonly ApplicationUser _user = new()
        {
            Id = Guid.NewGuid(), FirstName = "Nada", LastName = "Samir", Email = "new@test.com", IsActive = true,
        };
        protected readonly FrontendLinks _links = new(Options.Create(new FrontendSettings { BaseUrl = FrontendUrl }));

        protected AccountEmailHandlerTestsBase()
        {
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
            _userManager.GenerateEmailConfirmationTokenAsync(_user).Returns(RawToken);
            _userManager.GeneratePasswordResetTokenAsync(_user).Returns(RawToken);
        }
    }
}
