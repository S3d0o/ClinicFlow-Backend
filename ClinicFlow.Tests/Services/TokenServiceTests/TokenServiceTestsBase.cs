using System.Security.Cryptography;
using System.Text;
using Domain.Entities.IdentityModule;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Settings;

namespace ClinicFlow.Tests.Services.TokenServiceTests
{
    public abstract class TokenServiceTestsBase
    {
        protected const string Ip = "10.0.0.1";
        protected static readonly DateTime AccessTokenExpiresAt = new(2026, 10, 23, 12, 15, 0, DateTimeKind.Utc);

        // Dependencies
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly IUnitOfWork _uow;
        protected readonly IAccessTokenFactory _accessTokenFactory;
        protected readonly ILogger<TokenService> _logger;
        protected readonly IClock _clock;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        protected readonly JwtSettings _jwt = new()
        {
            RefreshTokenExpirationDays = 7,
            TheftDetectionWindowMinutes = 1,
        };

        // System Under Test
        protected readonly TokenService _sut;

        protected TokenServiceTestsBase()
        {
            _userManager = Substitute.For<UserManager<ApplicationUser>>(
                Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
            _uow = Substitute.For<IUnitOfWork>();
            _accessTokenFactory = Substitute.For<IAccessTokenFactory>();
            _logger = Substitute.For<ILogger<TokenService>>();

            _accessTokenFactory
                .Create(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
                .Returns(new AccessToken("access-token", AccessTokenExpiresAt));

            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _sut = new TokenService(Options.Create(_jwt), _userManager, _uow, _accessTokenFactory, _clock, _logger);
        }

        // Refresh tokens are stored as the Base64 SHA-256 of the raw value
        protected static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
