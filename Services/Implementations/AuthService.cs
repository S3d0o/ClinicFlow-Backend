using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore.Query.Internal;
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
            await using var transaction = await uow.BeginTransactionAsync();
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
                    await transaction.RollbackAsync();
                    logger.LogWarning("Doctor registration failed for {Email}: {Errors}",
                         request.Email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                    return AuthErrors.RegistrationFailed(createResult.Errors.Select(e => e.Description));
                }

                var roleResult = await userManager.AddToRoleAsync(user, "Doctor");

                if (!roleResult.Succeeded)
                {
                    await transaction.RollbackAsync();

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

                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = Uri.EscapeDataString(token);
                await emailService.SendEmailConfirmationAsync(user.Email, user.Id, encodedToken);

                logger.LogInformation("Doctor registered successfully: {Email}", request.Email);


                return new RegisterResponse(
                     "Registration successful. Please check your email to confirm your account.",
                       user.Email!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while registering doctor with email: {Email}", request.Email);
                await transaction.RollbackAsync();
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

            await using var transaction = await uow.BeginTransactionAsync();

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

                    await transaction.RollbackAsync();
                    return AuthErrors.RegistrationFailed(createResult.Errors.Select(e => e.Description));
                }

               var roleResult = await userManager.AddToRoleAsync(user, "Patient");
                if (!roleResult.Succeeded)
                {
                    logger.LogWarning("Failed to add Patient role to user {Email}: {Errors}",
                        request.Email, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                    await transaction.RollbackAsync();
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

                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var encodedToken = Uri.EscapeDataString(token);
                await emailService.SendEmailConfirmationAsync(user.Email, user.Id, encodedToken);

                logger.LogInformation("Patient registered successfully: {Email}", request.Email);

                return new RegisterResponse(
                         "Registration successful. Please check your email to confirm your account.",
                          user.Email!);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while registering patient with email: {Email}", request.Email);
                await transaction.RollbackAsync();
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
            var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
            if (!isPasswordValid)
            {
                logger.LogWarning("Login failed: Invalid password for email {Email}", request.Email);
                return AuthErrors.InvalidCredentials;
            }

            var roles = await userManager.GetRolesAsync(user);

            var tokens = await tokenService.GenerateTokensAsync(user, roles, ipAddress);

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

        public async Task<Result> LogoutAsync(RefreshTokenRequest request,string ipAddress, CancellationToken ct)
        {
            try
            {
                await tokenService.RevokeRefreshTokenAsync(request.RefreshToken, ipAddress, "Logout");
                logger.LogInformation("User logged out — refresh token revoked from {IpAddress}", ipAddress);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred during logout for IP {IpAddress}", ipAddress);
                return AuthErrors.InvalidRefreshToken; // used task.fromresult to return a completed task with the result, since the method signature requires a Task<Result>
            }
        }

        public async Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string ipAddress, CancellationToken ct)
        {
            try
            {
                var tokenResult = await tokenService.RefreshTokenAsync(request.RefreshToken, ipAddress);
                return tokenResult;
            }
            catch (Exception ex)
            {
                logger.LogError("An error occurred while refreshing token: {RefreshToken}. Error: {ErrorMessage}", request.RefreshToken, ex.Message);
                return AuthErrors.InvalidRefreshToken;
            }
        }

        public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(request.Email);

            if (user is null || !user.IsActive)
            {
                // Return Ok even if not found — prevents email enumeration attacks
                logger.LogInformation("ForgotPassword requested for unknown/inactive email {Email}", request.Email);
                return Result.Ok();
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(user);

            await emailService.SendPasswordResetAsync(request.Email,user.Id, Uri.EscapeDataString(token));
            
            logger.LogInformation(
                "[DEV ONLY] Password reset token for {Email}: userId={UserId} token={Token}",
                user.Email, user.Id, token);

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
            var activeRefreshTokens = await uow.RefreshTokens.GetActiveByUserIdAsync(user.Id);
            foreach (var token in activeRefreshTokens)
            {
                token.Revoked = DateTime.UtcNow;
                token.RevocationReason = "Password reset";
            }
            // Regenerate security stamp — invalidates all existing JWTs
            await userManager.UpdateSecurityStampAsync(user);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Password reset completed for {Email} — all sessions terminated", user.Email);
            return Result.Ok();

        }
    }
}
