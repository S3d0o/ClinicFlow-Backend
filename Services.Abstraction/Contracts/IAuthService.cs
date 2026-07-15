using Shared.DTOs.Auth;
using System.Globalization;

namespace Services.Abstraction.Contracts
{
    public interface IAuthService
    {
        Task<Result<RegisterResponse>> RegisterPatientAsync(RegisterPatientRequest request, CancellationToken ct);
        Task<Result<RegisterResponse>> RegisterDoctorAsync(RegisterDoctorRequest request, CancellationToken ct);
        Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string ipAddress, CancellationToken ct);
        Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string ipAddress, CancellationToken ct);
        Task<Result> LogoutAsync(RefreshTokenRequest request,string ipAddress, CancellationToken ct);
        Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct);
        Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);
        Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    }
}
