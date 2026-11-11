using Domain.Enums;
using System.Globalization;

namespace Services.Messaging
{
    // How dates and times are written in a message, per language (R10). The Arabic names are listed here rather than
    // taken from the machine's culture data, so every server writes the same text
    public static class MessageFormats
    {
        private static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

        private static readonly string[] ArabicMonths =
            ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

        // "Sunday, November 15, 2026" or "الأحد، 15 نوفمبر 2026"
        public static string Date(DateOnly date, Language language) => language == Language.En
            ? date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
            : $"{ArabicDays[(int)date.DayOfWeek]}، {date.Day} {ArabicMonths[date.Month - 1]} {date.Year}";

        // "3:30 PM" or "3:30 م"
        public static string Time(TimeOnly time, Language language) => language == Language.En
            ? time.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : $"{time.ToString("h:mm", CultureInfo.InvariantCulture)} {(time.Hour < 12 ? "ص" : "م")}";
    }
}
