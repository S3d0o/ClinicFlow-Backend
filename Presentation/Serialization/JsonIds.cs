using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Presentation.Serialization
{
    // API_CONTRACT.md §13.1 "Ids": on /api/v2 every id is a JSON string ("42"), in requests and responses, while the
    // database keys and the DTOs stay integers. An id is a property named Id or ending in Id (BranchId), or a collection
    // named ...Ids (ServiceIds), of type int, int? or a collection of int. Reading also accepts a JSON number.
    // Guid ids are strings already and are left alone. Applied by ApiJson.ConfigureV2 only, so inherited routes keep numbers
    public static class JsonIds
    {
        public static void WriteIdsAsStrings(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            foreach (var property in typeInfo.Properties)
            {
                if (property.CustomConverter is not null)
                    continue;

                var clrName = (property.AttributeProvider as MemberInfo)?.Name ?? property.Name;
                if (ConverterFor(clrName, property.PropertyType) is { } converter)
                    property.CustomConverter = converter;
            }
        }

        // Ordinal, so "IsPaid" or "Valid" isn't an id
        private static bool IsIdName(string name)
            => name == "Id" || name.EndsWith("Id", StringComparison.Ordinal);

        private static bool IsIdsName(string name)
            => name.EndsWith("Ids", StringComparison.Ordinal);

        private static JsonConverter? ConverterFor(string name, Type type)
        {
            if (IsIdName(name))
            {
                if (type == typeof(int)) return IntIdConverter.Instance;
                if (type == typeof(int?)) return NullableIntIdConverter.Instance;
                return null;
            }

            // int[], List<int>, IEnumerable<int>, IReadOnlyList<int>, IReadOnlyCollection<int>, IList<int>, ICollection<int>
            if (IsIdsName(name) && typeof(IEnumerable<int>).IsAssignableFrom(type)
                && (type == typeof(int[]) || type.IsAssignableFrom(typeof(List<int>))))
                return (JsonConverter)Activator.CreateInstance(typeof(IntIdCollectionConverter<>).MakeGenericType(type))!;

            return null;
        }

        internal static int ReadId(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetInt32();

            if (reader.TokenType == JsonTokenType.String
                && int.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return id;

            throw new JsonException("An id must be a string of digits, such as \"42\".");
        }

        internal static void WriteId(Utf8JsonWriter writer, int id)
            => writer.WriteStringValue(id.ToString(CultureInfo.InvariantCulture));

        private sealed class IntIdConverter : JsonConverter<int>
        {
            public static readonly IntIdConverter Instance = new();

            public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => ReadId(ref reader);

            public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
                => WriteId(writer, value);
        }

        // null stays null in both directions (a value-type converter is handed nulls itself)
        private sealed class NullableIntIdConverter : JsonConverter<int?>
        {
            public static readonly NullableIntIdConverter Instance = new();

            public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => reader.TokenType == JsonTokenType.Null ? null : ReadId(ref reader);

            public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
            {
                if (value is { } id)
                    WriteId(writer, id);
                else
                    writer.WriteNullValue();
            }
        }

        private sealed class IntIdCollectionConverter<TCollection> : JsonConverter<TCollection>
            where TCollection : IEnumerable<int>
        {
            public override TCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                    throw new JsonException("A list of ids must be a JSON array of strings, such as [\"1\", \"2\"].");

                var ids = new List<int>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    ids.Add(ReadId(ref reader));

                return typeof(TCollection) == typeof(int[]) ? (TCollection)(object)ids.ToArray() : (TCollection)(object)ids;
            }

            public override void Write(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (var id in value)
                    WriteId(writer, id);
                writer.WriteEndArray();
            }
        }
    }
}
