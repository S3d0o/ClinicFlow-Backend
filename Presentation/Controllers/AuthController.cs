// Presentation/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Presentation.Extensions;
using Services.Abstraction.Contracts;
using Shared.DTOs.Auth;

namespace Presentation.Controllers;

[Route("api/auth")]
public class AuthController(IAuthService service) : ApiController
{
    /// <summary>Register a new patient</summary>
    /// <remarks>Creates a patient account. Email confirmation required before login.</remarks>
    [AllowAnonymous]
    [HttpPost("register/patient")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterResponse>> RegisterPatient(
        [FromBody] RegisterPatientRequest request, CancellationToken ct)
    {
        var result = await service.RegisterPatientAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Register a new doctor</summary>
    /// <remarks>Creates a doctor account. Requires admin approval before appearing in search.</remarks>
    [AllowAnonymous]
    [HttpPost("register/doctor")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterResponse>> RegisterDoctor(
        [FromBody] RegisterDoctorRequest request, CancellationToken ct)
    {
        var result = await service.RegisterDoctorAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Login</summary>
    /// <remarks>Returns JWT access token and refresh token. Email must be confirmed first.</remarks>
    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIpAddress() ?? string.Empty; 
        var result = await service.LoginAsync(request, ip, ct);
        return HandleResult(result);
    }

    /// <summary>Refresh access token</summary>
    /// <remarks>Exchange a valid refresh token for a new access token + refresh token pair.</remarks>
    [AllowAnonymous]
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> RefreshToken(
        [FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIpAddress();
        var result = await service.RefreshTokenAsync(request, ip, ct);
        return HandleResult(result);
    }

    /// <summary>Logout</summary>
    /// <remarks>
    /// Revokes the refresh token. Works without an access token, so a user whose access token has expired
    /// can still log out; with a valid access token, the refresh token must belong to the caller.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIpAddress();
        var result = await service.LogoutAsync(User.FindUserId(), request, ip, ct);
        return HandleResult(result);
    }

    /// <summary>Confirm email</summary>
    /// <remarks>Validates the email confirmation token sent after registration.</remarks>
    [AllowAnonymous]
    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        var result = await service.ConfirmEmailAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Forgot password</summary>
    /// <remarks>
    /// Sends a password reset email. Always returns 204 regardless of whether
    /// the email exists — prevents email enumeration.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await service.ForgotPasswordAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Reset password</summary>
    /// <remarks>Resets the password using the token from the reset email. Revokes all active sessions.</remarks>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await service.ResetPasswordAsync(request, ct);
        return HandleResult(result);
    }
}
