using Microsoft.Extensions.Options;
using Shared.Settings;

namespace Services.Messaging
{
    // The frontend routes that messages link to (API contract §8). The frontend uses hash routing, so the query
    // string sits after "#/". A token is inserted as given: the caller has already URL-escaped it
    public class FrontendLinks(IOptions<FrontendSettings> options)
    {
        private readonly string _baseUrl = options.Value.BaseUrl.TrimEnd('/');

        public string ConfirmEmail(Guid userId, string escapedToken)
            => $"{_baseUrl}/#/confirm-email?userId={userId}&token={escapedToken}";

        public string ResetPassword(Guid userId, string escapedToken)
            => $"{_baseUrl}/#/reset-password?userId={userId}&token={escapedToken}";
    }
}
