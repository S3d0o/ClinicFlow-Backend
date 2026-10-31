using Domain.Entities.IdentityModule;
using Domain.Entities.Staff;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Shared.DTOs.Auth;
using Shared.Enums;

namespace Services.Implementations
{
    public class AuthService(
        UserManager<ApplicationUser> userManager,
        ILogger<AuthService> logger,
        IUnitOfWork uow,
        ITokenService tokenService,
        IUserAccessService userAccess,
        ITenantContext tenant,
        IClock clock) : IAuthService
    {
        public async Task<Result<RegisterResponse>> RegisterDoctorAsync(RegisterDoctorRequest request, CancellationToken ct)
        {
            if (await userManager.FindByEmailAsync(request.Email) != null)
            {
                logger.LogWarning("Attempt to register a doctor with an existing email: {Email}", request.Email);
                return AuthErrors.EmailAlreadyExists;
            }

            // An unknown or inactive specialty is the caller's mistake (404), not a server error from the foreign key (REVIEW.md M3)
            if (await uow.Specialties.GetByIdAsync(request.SpecialtyId, ct) is null)
            {
                logger.LogWarning("Doctor registration for {Email} rejected: specialty {SpecialtyId} not found", request.Email, request.SpecialtyId);
                return SpecialtyErrors.NotFound(request.SpecialtyId);
            }

            var user = NewUser(request.FirstName, request.LastName, request.Email, request.PhoneNumber, request.Gender, request.DateOfBirth);
            return await RegisterAsync(user, request.Password, Roles.Doctor, async token =>
            {
                // A doctor is a staff member at the default branch (P1 PR 4); the token's Doctor role comes from it
                var staff = new StaffMember
                {
                    UserId = user.Id,
                    Status = StaffStatus.Active,
                    Assignments = [new StaffRoleAssignment { Role = Roles.Doctor, BranchId = tenant.DefaultBranchId }],
                };
                uow.Staff.Add(staff);
                await uow.SaveChangesAsync(token);

                await uow.Doctors.AddAsync(new DoctorProfile
                {
                    UserId = user.Id,
                    StaffMemberId = staff.Id,
                    SpecialtyId = request.SpecialtyId,
                    Bio = request.Bio,
                    YearsOfExperience = request.YearsOfExperience,
                    ConsultationFee = request.ConsultationFee,
                    ClinicAddress = request.ClinicAddress,
                    ClinicCity = request.ClinicCity,
                    IsApprovedByAdmin = false, // can log in and set up schedules, but stays hidden from patients until an admin approves (REVIEW.md M1)
                }, token);
            }, ct);
        }

        public async Task<Result<RegisterResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct)
        {
            if (await userManager.FindByEmailAsync(request.Email) != null)
            {
                logger.LogWarning("Attempt to register a patient with an existing email: {Email}", request.Email);
                return AuthErrors.EmailAlreadyExists;
            }

            var user = NewUser(request.FirstName, request.LastName, request.Email, request.PhoneNumber, request.Gender, request.DateOfBirth);
            return await RegisterAsync(user, request.Password, Roles.Patient, token => uow.Patients.AddAsync(new PatientProfile
            {
                UserId = user.Id,
                BloodType = request.BloodType,
                Allergies = request.Allergies,
                ChronicConditions = request.ChronicConditions,
                EmergencyContactName = request.EmergencyContactName,
                EmergencyContactPhone = request.EmergencyContactPhone
            }, token), ct);
        }

        private ApplicationUser NewUser(string firstName, string lastName, string email, string phoneNumber, Gender gender, DateOnly? dateOfBirth)
            => new()
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                UserName = email,
                PhoneNumber = phoneNumber,
                Gender = gender,
                DateOfBirth = dateOfBirth,
                CreatedAt = clock.UtcNow,
                IsActive = true,
                EmailConfirmed = false,
            };

        // Shared by both registrations. The user, the role and the profile are created in one transaction,
        // so a failure at any step leaves no account without a role or profile. addProfileAsync adds the
        // role's profile (user.Id is set once CreateAsync succeeds); the confirmation email goes through the outbox,
        // saved in the same transaction, so a retried transaction can't send it twice and a rolled-back one sends nothing.
        private async Task<Result<RegisterResponse>> RegisterAsync(
            ApplicationUser user, string password, string role, Func<CancellationToken, Task> addProfileAsync, CancellationToken ct)
        {
            Result registered;
            try
            {
                registered = await uow.ExecuteInTransactionAsync<Result>(async transaction =>
                {
                    var createResult = await userManager.CreateAsync(user, password);
                    if (!createResult.Succeeded)
                    {
                        logger.LogWarning("{Role} registration failed for {Email}: {Errors}",
                            role, user.Email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                        return AuthErrors.RegistrationFailed(createResult.Errors.Select(e => e.Description));
                    }

                    var roleResult = await userManager.AddToRoleAsync(user, role);
                    if (!roleResult.Succeeded)
                    {
                        logger.LogWarning("Failed to add {Role} role to user {Email}: {Errors}",
                            role, user.Email, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                        // A missing role is a server problem, not something the caller can fix
                        return AuthErrors.RegistrationError;
                    }

                    await addProfileAsync(ct);
                    // Saved with the account: the confirmation email goes out if, and only if, the registration commits
                    uow.Outbox.Add(new EmailConfirmationRequested(user.Id));
                    await uow.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return Result.Ok();
                }, ct);
            }
            catch (Exception ex)
            {
                // The transaction has been rolled back
                logger.LogError(ex, "An error occurred while registering {Role} with email: {Email}", role, user.Email);
                return AuthErrors.RegistrationError;
            }

            if (registered.IsFailure)
                return Result<RegisterResponse>.Fail(registered.Errors);

            logger.LogInformation("{Role} registered successfully: {Email}", role, user.Email);
            return new RegisterResponse(
                "Registration successful. Please check your email to confirm your account.",
                user.Email!);
        }

        // ConfirmEmailAsync is responsible for confirming a user's email address using a token. It retrieves the user by ID, decodes the token,
        // and attempts to confirm the email. If successful, it returns a success result; otherwise, it logs the failure and returns an appropriate error.
        public async Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                logger.LogWarning("Email confirmation failed: User not found for ID {UserId}", request.UserId);
                return AuthErrors.UserNotFound;
            }

            // Token with url encoding might need to be decoded before use
            var decodedToken = Uri.UnescapeDataString(request.Token);

            var result = await userManager.ConfirmEmailAsync(user, decodedToken);

            if (!result.Succeeded)
            {
                logger.LogWarning("Email confirmation failed for {Email}", user.Email);
                return AuthErrors.InvalidEmailConfirmationToken;
            }
            logger.LogInformation("Email confirmed for {Email}", user.Email);
            return Result.Ok();
        }

        public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string ipAddress, CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(request.Email);

            if (user == null)
            {
                logger.LogWarning("Login failed: User not found for email {Email}", request.Email);
                return AuthErrors.InvalidCredentials;
            }
            if (await userManager.IsLockedOutAsync(user))
            {
                logger.LogWarning("Login failed: Account locked out for email {Email}", request.Email);
                return AuthErrors.AccountLockedOut;
            }

            // Password first: account state is only revealed to someone who knows the password
            var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
            if (!isPasswordValid)
            {
                // Counts toward Identity lockout (see Lockout options in AddInfraStructureServices)
                await userManager.AccessFailedAsync(user);
                logger.LogWarning("Login failed: Invalid password for email {Email}", request.Email);
                return AuthErrors.InvalidCredentials;
            }
            await userManager.ResetAccessFailedCountAsync(user);

            if(!user.IsActive)
            {
                logger.LogWarning("Login failed: User is not active for email {Email}", request.Email);
                return AuthErrors.AccountNotActive;
            }
            if(!user.EmailConfirmed)
            {
                logger.LogWarning("Login failed: Email not confirmed for email {Email}", request.Email);
                return AuthErrors.EmailNotConfirmed;
            }

            var access = await userAccess.GetAsync(user, ct);

            var tokens = await tokenService.GenerateTokensAsync(user, access, ipAddress, ct);

            return new LoginResponse
                (
                 tokens.AccessToken,
                 tokens.RefreshToken,
                 tokens.AccessTokenExpiresAt,
                     user.Email!,
                     $"{user.FirstName} {user.LastName}",
                    access.PrimaryRole
                );
        }

        public async Task<Result> LogoutAsync(Guid? userId, RefreshTokenRequest request, string ipAddress, CancellationToken ct)
        {
            var result = await tokenService.RevokeRefreshTokenAsync(userId, request.RefreshToken, ipAddress, "Logout", ct);
            if (result.IsSuccess)
                logger.LogInformation("Logout revoked a refresh token from {IpAddress} (caller {UserId}; null when anonymous)", ipAddress, userId);
            return result;
        }

        // TokenService returns expected failures as Result; unexpected exceptions are not swallowed,
        // so a database outage is not reported to the client as an invalid token (401)
        public Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string ipAddress, CancellationToken ct)
            => tokenService.RefreshTokenAsync(request.RefreshToken, ipAddress, ct);

        public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(request.Email);

            if (user is null || !user.IsActive)
            {
                // Return Ok even if not found — prevents email enumeration attacks
                logger.LogInformation("ForgotPassword requested for unknown/inactive email {Email}", request.Email);
                return Result.Ok();
            }

            // Only an outbox row is written; the handler creates the token and sends the email, so the response takes
            // about as long for a registered email as for an unknown one (REVIEW.md NL24)
            uow.Outbox.Add(new PasswordResetRequested(user.Id));
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Password reset email queued for user {UserId}", user.Id);

            return Result.Ok();
        }

        public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(request.UserId);
            if (user is null || !user.IsActive)
            {
                logger.LogWarning("ResetPassword failed: User not found or inactive for ID {UserId}", request.UserId);
                return AuthErrors.UserNotFound;
            }
            var decodedToken = Uri.UnescapeDataString(request.Token);
            var result = await userManager.ResetPasswordAsync(user,decodedToken,request.NewPassword);
            if (!result.Succeeded)
            {
                logger.LogWarning("ResetPassword failed for user {Email}: {Errors}", user.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                return AuthErrors.InvalidPasswordResetToken;
            }

            // revoke all existing refresh tokens for the user after password reset
            // user must re-login to get new tokens after password reset on all devices
            await uow.RefreshTokens.RevokeAllActiveAsync(user.Id, "Password reset", null, clock.UtcNow, ct);

            // Regenerate security stamp — refresh tokens carry the stamp, so any token issued
            // before this point is rejected on refresh. Access tokens (15 min) stay valid until expiry.
            await userManager.UpdateSecurityStampAsync(user);

            logger.LogInformation("Password reset completed for {Email} — all sessions terminated", user.Email);
            return Result.Ok();

        }
    }
}
