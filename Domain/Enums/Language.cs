namespace Domain.Enums
{
    // A user's preferred language (FR-ACC-06) and the language of a message template. Arabic is the default.
    // Stored and sent as its code, "ar" or "en" (LanguageCodes), not as the member name
    public enum Language
    {
        Ar = 0,
        En = 1
    }

    public static class LanguageCodes
    {
        public const string Arabic = "ar";
        public const string English = "en";

        public static string Code(this Language language) => language == Language.En ? English : Arabic;

        // Anything but "en" is Arabic, the default
        public static Language Parse(string? code)
            => string.Equals(code, English, StringComparison.OrdinalIgnoreCase) ? Language.En : Language.Ar;
    }
}
