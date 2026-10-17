using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.DTOs.Profile;
using Shared.Enums;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.ProfileServiceTests
{
    public class GetProfileAsyncTests : ProfileServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task GetProfileAsync_WhenFound_ReturnsProfileWithFirstRole()
        {
            var createdAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), FirstName = "Omar", LastName = "Fathy", Email = "omar@test.com", PhoneNumber = "+201009876543",
                ProfilePictureUrl = null, Gender = Gender.Male, DateOfBirth = new DateOnly(1980, 7, 2), CreatedAt = createdAt
            };
            _userManager.FindByIdAsync(user.Id.ToString()).Returns(user);
            _userManager.GetRolesAsync(user).Returns(new List<string> { "Doctor" });

            var result = await _sut.GetProfileAsync(user.Id, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new UserProfileResponse(user.Id, "Omar", "Fathy", "omar@test.com", "+201009876543", null,
                Gender.Male, new DateOnly(1980, 7, 2), "Doctor", createdAt), result.Value);
        }

        [Fact]
        public async Task GetProfileAsync_WhenUserNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.GetProfileAsync(Guid.NewGuid(), _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(ProfileErrors.NotFound, result.Errors);
        }
    }
}
