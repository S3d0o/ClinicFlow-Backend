using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.ProfileServiceTests
{
    public abstract class ProfileServiceTestsBase
    {
        // Dependencies
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly ILogger<ProfileService> _logger;

        // System Under Test
        protected readonly ProfileService _sut;

        protected ProfileServiceTestsBase()
        {
            _userManager = Substitute.For<UserManager<ApplicationUser>>(
                Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
            _logger = Substitute.For<ILogger<ProfileService>>();

            _sut = new ProfileService(_userManager, _logger);
        }
    }
}
