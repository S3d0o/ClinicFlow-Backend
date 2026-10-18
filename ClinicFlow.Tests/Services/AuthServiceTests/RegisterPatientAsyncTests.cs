using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Auth;
using Shared.Enums;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class RegisterPatientAsyncTests : AuthServiceTestsBase
    {
        private const string _email = "new.patient@test.com";
        private const string _password = "Patient@1234";
        private const string _rawToken = "conf+token/=";
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly Guid _userId = Guid.NewGuid();
        private ApplicationUser? _created;
        private readonly RegisterPatientRequest _request = new(
            "Mona", "Adel", _email, _password, "+201001234567", Gender.Female, new DateOnly(1995, 3, 14),
            BloodType.APositive, "Penicillin", "Asthma", "Adel Ali", "+201112223334");

        public RegisterPatientAsyncTests()
        {
            _userManager.FindByEmailAsync(_email).Returns((ApplicationUser?)null);
            _userManager.CreateAsync(Arg.Any<ApplicationUser>(), _password)
                .Returns(IdentityResult.Success)
                .AndDoes(ci =>
                {
                    _created = ci.ArgAt<ApplicationUser>(0)!;
                    _created.Id = _userId; // the store assigns the id
                });
            _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Patient").Returns(IdentityResult.Success);
            _userManager.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>()).Returns(_rawToken);
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenValid_CreatesUnconfirmedActiveUserFromRequest()
        {
            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            var created = _created;
            Assert.NotNull(created);
            Assert.Equal("Mona", created!.FirstName);
            Assert.Equal("Adel", created.LastName);
            Assert.Equal(_email, created.Email);
            Assert.Equal(_email, created.UserName);
            Assert.Equal("+201001234567", created.PhoneNumber);
            Assert.Equal(Gender.Female, created.Gender);
            Assert.Equal(new DateOnly(1995, 3, 14), created.DateOfBirth);
            Assert.False(created.EmailConfirmed);
            Assert.True(created.IsActive);
            await _userManager.Received(1).AddToRoleAsync(created, "Patient");
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenValid_AddsPatientProfileAndCommits()
        {
            PatientProfile? profile = null;
            await _uow.Patients.AddAsync(Arg.Do<PatientProfile>(p => profile = p), Arg.Any<CancellationToken>());

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(profile);
            Assert.Equal(_userId, profile!.UserId);
            Assert.Equal(BloodType.APositive, profile.BloodType);
            Assert.Equal("Penicillin", profile.Allergies);
            Assert.Equal("Asthma", profile.ChronicConditions);
            Assert.Equal("Adel Ali", profile.EmergencyContactName);
            Assert.Equal("+201112223334", profile.EmergencyContactPhone);
            await _uow.Received(1).SaveChangesAsync(_ct);
            await _transaction.Received(1).CommitAsync(_ct);
            await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenValid_SendsConfirmationEmailWithEscapedToken()
        {
            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(_email, result.Value.Email);
            await _emailService.Received(1).SendEmailConfirmationAsync(
                _email, _userId, Uri.EscapeDataString(_rawToken), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenEmailAlreadyExists_ReturnsEmailAlreadyExistsError()
        {
            _userManager.FindByEmailAsync(_email).Returns(new ApplicationUser { Email = _email });

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.EmailAlreadyExists, result.Errors);
            await _uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
            await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenIdentityRejectsUser_ReturnsRegistrationFailedAndRollsBack()
        {
            _userManager.CreateAsync(Arg.Any<ApplicationUser>(), _password)
                .Returns(Failed("Passwords must have at least one digit.", "Username is invalid."));

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.RegistrationFailed(["Passwords must have at least one digit.", "Username is invalid."]), result.Errors);
            await _transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
            await _userManager.DidNotReceive().AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
            await _uow.Patients.DidNotReceive().AddAsync(Arg.Any<PatientProfile>(), Arg.Any<CancellationToken>());
            await _emailService.DidNotReceive().SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenRoleAssignmentFails_ReturnsRegistrationErrorAndRollsBack()
        {
            _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), "Patient").Returns(Failed("Role PATIENT does not exist."));

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsFailure);
            // A missing role is a server problem (500), not the caller's (REVIEW.md M3)
            Assert.Contains(AuthErrors.RegistrationError, result.Errors);
            await _transaction.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
            await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
            await _uow.Patients.DidNotReceive().AddAsync(Arg.Any<PatientProfile>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenSavingProfileThrows_ReturnsRegistrationErrorAndRollsBack()
        {
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.RegistrationError, result.Errors);
            // The rollback must run even if the request was cancelled
            await _transaction.Received(1).RollbackAsync(CancellationToken.None);
            await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
            await _emailService.DidNotReceive().SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenConfirmationEmailFails_StillReturnsSuccessWithoutRollback()
        {
            _emailService.SendEmailConfirmationAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _transaction.Received(1).CommitAsync(_ct);
            await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RegisterPatientAsync_WhenConfirmationTokenGenerationFails_StillReturnsSuccess()
        {
            _userManager.GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>())
                .ThrowsAsync(new InvalidOperationException("No token provider"));

            var result = await _sut.RegisterPatientAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        }
    }
}
