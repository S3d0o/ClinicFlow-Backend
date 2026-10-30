using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Presentation.Serialization
{
    // The JSON settings of the API. Every route uses Configure (through AddJsonOptions in the host); /api/v2 routes use
    // CreateV2Options, which starts from the same settings and adds the §13.1 rules. Put any new app-wide JSON setting
    // in Configure, never straight into AddJsonOptions, so the V2 options keep matching the global ones
    public static class ApiJson
    {
        public static void Configure(JsonSerializerOptions options)
        {
            // Enums are strings (contract §1); integers are still accepted on input
            options.Converters.Add(new JsonStringEnumConverter());
        }

        // API_CONTRACT.md §13.1: ids are JSON strings (JsonIds) and times of day are "HH:mm" (TimeOfDayJsonConverter)
        public static MvcJsonOptions CreateV2Options(MvcJsonOptions global)
        {
            var v2 = new MvcJsonOptions { AllowInputFormatterExceptionMessages = global.AllowInputFormatterExceptionMessages };
            ConfigureV2(v2.JsonSerializerOptions);
            return v2;
        }

        public static void ConfigureV2(JsonSerializerOptions options)
        {
            Configure(options);
            options.Converters.Add(new TimeOfDayJsonConverter());
            options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                .WithAddedModifier(JsonIds.WriteIdsAsStrings);
        }
    }
}
