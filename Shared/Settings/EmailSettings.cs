namespace Shared.Settings
{
    public class EmailSettings
    {
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string FromName { get; init; } = string.Empty;
        public string FromAddress { get; init; } = string.Empty;

        // The EmailSettings keys an SMTP send can't work without; empty when email is configured.
        // Program.cs logs them at startup instead of failing, since the app runs without email (REVIEW.md NL12)
        public IReadOnlyList<string> MissingSettings()
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(Host)) missing.Add(nameof(Host));
            if (Port <= 0) missing.Add(nameof(Port));
            if (string.IsNullOrWhiteSpace(Username)) missing.Add(nameof(Username));
            if (string.IsNullOrWhiteSpace(Password)) missing.Add(nameof(Password));
            if (string.IsNullOrWhiteSpace(FromAddress)) missing.Add(nameof(FromAddress));
            return missing;
        }
    }
}
