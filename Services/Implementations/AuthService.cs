using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Shared.DTOs.Auth;

namespace Services.Implementations
{
    public class AuthService(
        UserManager<ApplicationUser> userManager,
        ILogger<AuthService> logger,
        IUnitOfWork uow,
        ITokenService tokenService,
        IEmailService emailService) : IAuthService
    {
        public async Task<Result<RegisterResponse>> RegisterDoctorAsync(RegisterDoctorRequest request, CancellationToken ct)
        {
            if (await userManager.FindByEmailAsync(request.Email) != null)
            {
                logger.LogWarning("Attempt to register a doctor with an existing email: {Email}", request.Email);
                return AuthErrors.EmailAlreadyExists;
            }

            // i did this cause i want to make sure that if any of the operations fail,
            // the entire registration process is rolled back to maintain data integrity.
            // if user is created and roles is assigned but doctor profile creation fails, we don't want to have a user without a profile.
            await using var transaction = await uow.BeginTransactionAsync(ct);
            try
            {
                var user = new ApplicationUser
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    UserName = request.Email,
                    PhoneNumber = request.PhoneNumber,
                    EmailConfirmed = false,
                    Gender = request.Gender,
                    DateOfBirth = request.DateOfBirth,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                };
                var createResult = await userManager.CreateAsync(user, request.Password);

                if (!createResult.Succeeded)
                {
                    await transaction.RollbackAsync(ct);
                    logger.LogWarning("Doctor registration failed for {Email}: {Errors}",
                         request.Email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                    return AuthErrors.RegistrationFailed(createResult.Errors.Select(e => e.Description));
                }

                var roleResult = await userManager.AddToRoleAsync(user, "Doctor");

                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync(ct);

                    logger.LogWarning(
                        "Failed to add Doctor role to user {Email}: {Errors}",
                        request.Email,
                        string.Join(", ", roleResult.Errors.Select(e => e.Description)));

                    return AuthErrors.RegistrationFailed(
                        roleResult.Errors.Select(e => e.Description));
                }

                var doctorProfile = new DoctorProfile
                {
                    UserId = user.Id,
                    SpecialtyId = request.SpecialtyId,
                    Bio = request.Bio,
                    YearsOfExperience = request.YearsOfExperience,
                    ConsultationFee = request.ConsultationFee,
                    ClinicAddress = request.ClinicAddress,
                    ClinicCity = request.ClinicCity,
                    IsApprovedByAdmin = false, // doctors need to be approved by admin before they can log in
                };

                await uow.Doctors.AddAsync(doctorProfile, ct);
                await uow.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);

                await TrySendConfirmationEmailAsync(user);

                logger.LogInformation("Doctor registered successfully: {Email}", request.Email);


                return new RegisterResponse(
                     "Registration successful. Please check your email to confirm your account.",
                       user.Email!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while registering doctor with email: {Email}", request.Email);
                await transaction.RollbackAsync(CancellationToken.None); // must run even if the request was cancelled
                return AuthErrors.RegistrationFailed(["An error occurred during registration. Please try again later."]);
            }

        }

        public async Task<Result<RegisterResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct)
        {
            if (await userManager.FindByEmailAsync(request.Email) != null)
            {
                logger.LogWarning("Attempt to register a patient with an existing email: {Email}", request.Email);
                return AuthErrors.EmailAlreadyExists;
            }

            await using var transaction = await uow.BeginTransactionAsync(ct);

            try
            {
                var user = new ApplicationUser
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = request.Email,
                    UserName = request.Email,
                    PhoneNumber = request.PhoneNumber,
                    Gender = request.Gender,
                    DateOfBirth = request.DateOfBirth,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    EmailConfirmed = false,
                };
                var createResult = await userManager.CreateAsync(user, request.Password);

                if (!createResult.Succeeded)
                {
                    logger.LogWarning("Patient registration failed for {Email}: {Errors}",
                        request.Email, string.Join(", ", createResult.Errors.Select(e => e.Description)));

                    await transaction.RollbackAsync(ct);
                    return AuthErrors.RegistrationFailed(createResult.Errors.Select(e => e.Description));
                }

               var roleResult = await userManager.AddToRoleAsync(user, "Patient");
                if (!roleResult.Succeeded)
                {
                    logger.LogWarning("Failed to add Patient role to user {Email}: {Errors}",
                        request.Email, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                    await transaction.RollbackAsync(ct);
                    return AuthErrors.RegistrationFailed(roleResult.Errors.Select(e => e.Description));
                }


                var patientProfile = new PatientProfile
                {
                    UserId = user.Id,
                    BloodType = request.BloodType,
                    Allergies = request.Allergies,
                    ChronicConditions = request.ChronicConditions,
                    EmergencyContactName = request.EmergencyContactName,
                    EmergencyContactPhone = request.EmergencyContactPhone
                };

                await uow.Patients.AddAsync(patientProfile, ct);
                await uow.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);

                await TrySendConfirmationEmailAsync(user);

                logger.LogInformation("Patient registered successfully: {Email}", request.Email);

                return new RegisterResponse(
                         "Registration successful. Please check your email to confirm your account.",
                          user.Email!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while registering patient with email: {Email}", request.Email);
                await transaction.RollbackAsync(CancellationToken.None); // must run even if the request was cancelled
                return AuthErrors.RegistrationFailed(["An error occurred during registration. Please try again later."]);
            }
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

            var roles = await userManager.GetRolesAsync(user);

            var tokens = await tokenService.GenerateTokensAsync(user, roles, ipAddress, ct);

            return new LoginResponse
                (
                 tokens.AccessToken,
                 tokens.RefreshToken,
                 tokens.AccessTokenExpiresAt,
                     user.Email!,
                     $"{user.FirstName} {user.LastName}",
                    roles.FirstOrDefault() ?? string.Empty
                );
        }

        public async Task<Result> LogoutAsync(Guid userId, RefreshTokenRequest request, string ipAddress, CancellationToken ct)
        {
            var result = await tokenService.RevokeRefreshTokenAsync(userId, request.RefreshToken, ipAddress, "Logout", ct);
            if (result.IsSuccess)
                logger.LogInformation("User {UserId} logged out — refresh token revoked from {IpAddress}", userId, ipAddress);
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

            // A send failure is logged, not returned: a 500 here would only happen for registered,
            // active emails and reveal which emails exist (REVIEW.md NL24)
            try
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                await emailService.SendPasswordResetAsync(request.Email, user.Id, Uri.EscapeDataString(token), ct);
                logger.LogInformation("Password reset email sent for user {UserId}", user.Id);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to send password reset email to user {UserId}", user.Id);
            }

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
            await uow.RefreshTokens.RevokeAllActiveAsync(user.Id, "Password reset", null, ct);

            // Regenerate security stamp — refresh tokens carry the stamp, so any token issued
            // before this point is rejected on refresh. Access tokens (15 min) stay valid until expiry.
            await userManager.UpdateSecurityStampAsync(user);

            logger.LogInformation("Password reset completed for {Email} — all sessions terminated", user.Email);
            return Result.Ok();

        }

        // Registration is already committed when this runs: an SMTP failure is logged and must not turn
        // a successful registration into an error (or trigger a rollback of a committed transaction)
        private async Task TrySendConfirmationEmailAsync(ApplicationUser user)
        {
            try
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await emailService.SendEmailConfirmationAsync(user.Email!, user.Id, Uri.EscapeDataString(token));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send confirmation email to user {UserId}", user.Id);
            }
        }
    }
}
