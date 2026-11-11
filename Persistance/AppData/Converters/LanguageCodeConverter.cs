using Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Persistence.AppData.Converters
{
    // A Language column holds the code the contract uses, "ar" or "en", not the enum member's name
    public class LanguageCodeConverter() : ValueConverter<Language, string>(
        value => value.Code(),
        value => LanguageCodes.Parse(value));
}
