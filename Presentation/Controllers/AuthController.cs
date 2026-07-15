// Presentation/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Presentation.Extensions;
using Services.Abstraction.Contracts;
using Shared.DTOs.Auth;

namespace Presentation.Controllers;

[Route("api/auth")]
public class AuthController(IAuthService service) : ApiController
{
    /// <summary>Register a new patient</summary>
    /// <remarks>Creates a patient account. Email confirmation required before login.</remarks>
    [HttpPost("register/patient")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterResponse>> RegisterPatient(
        [FromBody] RegisterPatientRequest request, CancellationToken ct)
    {
        var result = await service.RegisterPatientAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Register a new doctor</summary>
    /// <remarks>Creates a doctor account. Requires admin approval before appearing in search.</remarks>
    [HttpPost("register/doctor")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RegisterResponse>> RegisterDoctor(
        [FromBody] RegisterDoctorRequest request, CancellationToken ct)
    {
        var result = await service.RegisterDoctorAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Login</summary>
    /// <remarks>Returns JWT access token and refresh token. Email must be confirmed first.</remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIpAddress();
        var result = await service.LoginAsync(request, ip, ct);
        return HandleResult(result);
    }

    /// <summary>Refresh access token</summary>
    /// <remarks>Exchange a valid refresh token for a new access token + refresh token pair.</remarks>
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
    /// <remarks>Revokes the refresh token. Requires a valid access token.</remarks>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIpAddress();
        var result = await service.LogoutAsync(request, ip, ct);
        return HandleResult(result);
    }

    /// <summary>Confirm email</summary>
    /// <remarks>Validates the email confirmation token sent after registration.</remarks>
    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
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
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await service.ForgotPasswordAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Reset password</summary>
    /// <remarks>Resets the password using the token from the reset email. Revokes all active sessions.</remarks>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await service.ResetPasswordAsync(request, ct);
        return HandleResult(result);
    }
}