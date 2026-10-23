using Domain.Entities.IdentityModule;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shared.ResultPattern;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public abstract class AuthServiceTestsBase
    {
        // Dependencies
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly ILogger<AuthService> _logger;
        protected readonly IUnitOfWork _uow;
        protected readonly ITokenService _tokenService;
        protected readonly IEmailService _emailService;
        protected readonly IClock _clock;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        protected readonly ITransaction _transaction;

        // System Under Test
        protected readonly AuthService _sut;

        protected AuthServiceTestsBase()
        {
            _userManager = Substitute.For<UserManager<ApplicationUser>>(
                Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
            _logger = Substitute.For<ILogger<AuthService>>();
            _uow = Substitute.For<IUnitOfWork>();
            _tokenService = Substitute.For<ITokenService>();
            _emailService = Substitute.For<IEmailService>();
            _transaction = Substitute.For<ITransaction>();

            _uow.RunsTransactionsWith<Result>(_transaction);

            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _sut = new AuthService(_userManager, _logger, _uow, _tokenService, _emailService, _clock);
        }

        protected static IdentityResult Failed(params string[] descriptions) =>
            IdentityResult.Failed(descriptions.Select(d => new IdentityError { Description = d }).ToArray());
    }
}
