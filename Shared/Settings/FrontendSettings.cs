namespace Shared.Settings
{
    public class FrontendSettings
    {
        // Used for CORS and for links in emails
        public string BaseUrl { get; init; } = "http://localhost:3000";
    }
}
