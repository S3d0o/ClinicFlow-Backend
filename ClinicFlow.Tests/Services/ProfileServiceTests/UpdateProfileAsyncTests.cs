using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Shared.DTOs.Profile;
using Shared.Enums;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.ProfileServiceTests
{
    public class UpdateProfileAsyncTests : ProfileServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly ApplicationUser _user;

        public UpdateProfileAsyncTests()
        {
            _user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                FirstName = "Mona",
                LastName = "Adel",
                Email = "mona@test.com",
                PhoneNumber = "+201001234567",
                ProfilePictureUrl = "https://cdn.test/old.png",
                Gender = Gender.Female,
                DateOfBirth = new DateOnly(1995, 3, 14),
                IsActive = true
            };
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
            _userManager.UpdateAsync(_user).Returns(IdentityResult.Success);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenAllFieldsSent_AppliesThemThroughUserManager()
        {
            var result = await _sut.UpdateProfileAsync(_user.Id, new UpdateUserProfileRequest
            {
                FirstName = "Mona", LastName = "Hassan", PhoneNumber = "+201009998887",
                ProfilePictureUrl = "https://cdn.test/new.png", Gender = Gender.Female, DateOfBirth = new DateOnly(1996, 1, 2)
            }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("Hassan", _user.LastName);
            Assert.Equal("+201009998887", _user.PhoneNumber);
            Assert.Equal("https://cdn.test/new.png", _user.ProfilePictureUrl);
            Assert.Equal(new DateOnly(1996, 1, 2), _user.DateOfBirth);
            await _userManager.Received(1).UpdateAsync(_user);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenFieldsNull_KeepsTheirCurrentValues()
        {
            var result = await _sut.UpdateProfileAsync(_user.Id, new UpdateUserProfileRequest { LastName = "Hassan" }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("Mona", _user.FirstName);
            Assert.Equal("Hassan", _user.LastName);
            Assert.Equal("+201001234567", _user.PhoneNumber);
            Assert.Equal("https://cdn.test/old.png", _user.ProfilePictureUrl);
            Assert.Equal(Gender.Female, _user.Gender);
            Assert.Equal(new DateOnly(1995, 3, 14), _user.DateOfBirth);
        }

        [Fact]
        public async Task UpdateProfileAsync_Always_LeavesEmailAndAccountStateUntouched()
        {
            // The request has no email field: changing the login email isn't possible through this endpoint
            await _sut.UpdateProfileAsync(_user.Id, new UpdateUserProfileRequest { FirstName = "Mai" }, _ct);

            Assert.Equal("mona@test.com", _user.Email);
            Assert.True(_user.IsActive);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenIdentityRejectsUpdate_ReturnsUpdateFailedError()
        {
            _userManager.UpdateAsync(_user).Returns(IdentityResult.Failed(new IdentityError { Description = "Concurrency failure" }));

            var result = await _sut.UpdateProfileAsync(_user.Id, new UpdateUserProfileRequest { FirstName = "Mai" }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(ProfileErrors.UpdateFailed, result.Errors);
        }

        [Fact]
        public async Task UpdateProfileAsync_WhenUserNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.UpdateProfileAsync(Guid.NewGuid(), new UpdateUserProfileRequest { FirstName = "Mai" }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(ProfileErrors.NotFound, result.Errors);
            await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
        }
    }
}
