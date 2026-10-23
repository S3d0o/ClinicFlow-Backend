using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Auth;
using Shared.Enums;
using Shared.Errors;
using Shared.ResultPattern;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class RegisterDoctorAsyncTests : AuthServiceTestsBase
    {
        private const string _email = "new.doctor@test.com";
        private const string _password = "Doctor@1234";
        private const string _rawToken = "conf+token/=";
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly Guid _userId = Guid.NewGuid();
        private ApplicationUser? _created;
        private readonly RegisterDoctorRequest _request = new(
            "Omar", "Fathy", _email, _password, "+201009876543", Gender.Male, new DateOnly(1980, 7, 2),
            3, "Cardiologist", 12, 450m, "12 Tahrir St", "Cairo");

        public RegisterDoctorAsyncTests()
        {
            _userManager.FindByEmailAsync(_email).Returns((ApplicationUser?)null);
            _uow.Specialties.GetByIdAsync(3, _ct).Returns(new Specialty { Id = 3, Name = "Cardiology", IsActive = true });
            _userManager.CreateAsync(Arg.Any<ApplicationUser>(), _password)
                .Returns(IdentityResult.Success)
                .AndDoes(ci =>
                {
                    _created = ci.ArgAt<ApplicationUser>(0)!;
                    _created.Id = _userId; // the store assigns the id
                });
            _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Doctor").Returns(IdentityResult.Success);
            _userManager.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns(_rawToken);
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenValid_CreatesUnconfirmedUserWithDoctorRole()
        {
            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(_created);
            Assert.Equal("Omar", _created!.FirstName);
            Assert.Equal("Fathy", _created.LastName);
            Assert.Equal(_email, _created.Email);
            Assert.Equal(_email, _created.UserName);
            Assert.False(_created.EmailConfirmed);
            Assert.True(_created.IsActive);
            await _userManager.Received(1).AddToRoleAsync(_created, "Doctor");
            await _userManager.DidNotReceive().AddToRoleAsync(Arg.Any<ApplicationUser>(), "Patient");
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenValid_AddsUnapprovedDoctorProfileAndCommits()
        {
            DoctorProfile? profile = null;
            await _uow.Doctors.AddAsync(Arg.Do<DoctorProfile>(p => profile = p), Arg.Any<CancellationToken>());

            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(profile);
            Assert.Equal(_userId, profile!.UserId);
            Assert.Equal(3, profile.SpecialtyId);
            Assert.Equal("Cardiologist", profile.Bio);
            Assert.Equal(12, profile.YearsOfExperience);
            Assert.Equal(450m, profile.ConsultationFee);
            Assert.Equal("12 Tahrir St", profile.ClinicAddress);
            Assert.Equal("Cairo", profile.ClinicCity);
            Assert.False(profile.IsApprovedByAdmin);
            await _uow.Received(1).SaveChangesAsync(_ct);
            await _transaction.Received(1).CommitAsync(_ct);
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenValid_SendsConfirmationEmailWithEscapedToken()
        {
            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(_email, result.Value.Email);
            await _emailService.Received(1).SendEmailConfirmationAsync(
                _email, _userId, Uri.EscapeDataString(_rawToken), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenValid_SendsTheEmailOnlyAfterTheCommit()
        {
            // A retried transaction re-runs its operation, so the email must stay outside it (REVIEW.md NL14)
            await _sut.RegisterDoctorAsync(_request, _ct);

            Received.InOrder(() =>
            {
                _transaction.CommitAsync(_ct);
                _emailService.SendEmailConfirmationAsync(_email, _userId, Arg.Any<string>(), Arg.Any<CancellationToken>());
            });
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenEmailAlreadyExists_ReturnsEmailAlreadyExistsError()
        {
            _userManager.FindByEmailAsync(_email).Returns(new ApplicationUser { Email = _email });

            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.EmailAlreadyExists, result.Errors);
            await _uow.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<Result>(default!, default);
            await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenSpecialtyUnknown_ReturnsSpecialtyNotFoundWithoutCreatingAnything()
        {
            // REVIEW.md M3: this used to fail on the foreign key and return 500
            var result = await _sut.RegisterDoctorAsync(_request with { SpecialtyId = 999 }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(SpecialtyErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<Result>(default!, default);
            await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenIdentityRejectsUser_ReturnsRegistrationFailedAndRollsBack()
        {
            _userManager.CreateAsync(Arg.Any<ApplicationUser>(), _password).Returns(Failed("Email 'new.doctor@test.com' is already taken."));

            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.RegistrationFailed(["Email 'new.doctor@test.com' is already taken."]), result.Errors);
            await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
            await _userManager.DidNotReceive().AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
            await _uow.Doctors.DidNotReceive().AddAsync(Arg.Any<DoctorProfile>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenRoleAssignmentFails_ReturnsRegistrationErrorAndRollsBack()
        {
            _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Doctor").Returns(Failed("Role DOCTOR does not exist."));

            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsFailure);
            // A missing role is a server problem (500), not the caller's (REVIEW.md M3)
            Assert.Contains(AuthErrors.RegistrationError, result.Errors);
            await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
            await _uow.Doctors.DidNotReceive().AddAsync(Arg.Any<DoctorProfile>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenSavingProfileFails_ReturnsRegistrationErrorAndRollsBack()
        {
            // e.g. an unknown SpecialtyId violates the foreign key
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new DbUpdateException("FK_DoctorProfiles_Specialties"));

            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.RegistrationError, result.Errors);
            await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
            await _emailService.DidNotReceive().SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterDoctorAsync_WhenConfirmationEmailFails_StillReturnsSuccessWithoutRollback()
        {
            _emailService.SendEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

            var result = await _sut.RegisterDoctorAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _transaction.Received(1).CommitAsync(_ct);
        }
    }
}
