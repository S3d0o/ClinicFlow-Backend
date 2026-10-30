using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Presentation.Serialization
{
    // API_CONTRACT.md §13.1 "Dates and times": on /api/v2 a TimeOnly (startTime, endTime: branch wall clock) is written
    // as "HH:mm" and read from "HH:mm" or "HH:mm:ss". Inherited routes keep "HH:mm:ss" (§1). Covers TimeOnly? too
    public sealed class TimeOfDayJsonConverter : JsonConverter<TimeOnly>
    {
        private static readonly string[] ReadFormats = ["HH:mm", "HH:mm:ss"];

        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String
                && TimeOnly.TryParseExact(reader.GetString(), ReadFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return time;

            throw new JsonException("A time must be \"HH:mm\" or \"HH:mm:ss\", such as \"09:30\".");
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
}
