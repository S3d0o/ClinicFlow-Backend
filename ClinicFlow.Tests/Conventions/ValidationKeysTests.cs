using Presentation.Filters;

namespace ClinicFlow.Tests.Conventions
{
    // Validation error keys are camelCase property paths on every route (P1 plan Q-F, contract §3.1 B and §13.1)
    public class ValidationKeysTests
    {
        [Theory]
        [InlineData("Email", "email")]
        [InlineData("SlotId", "slotId")]
        [InlineData("ReasonForVisit", "reasonForVisit")]
        [InlineData("Lines[0].UnitPrice", "lines[0].unitPrice")]
        [InlineData("Patient.Address.City", "patient.address.city")]
        [InlineData("Items[12]", "items[12]")]
        [InlineData("email", "email")]
        [InlineData("", "")]
        public void ToCamelCase_PropertyPath_IsTheJsonPath(string propertyPath, string expected)
            => Assert.Equal(expected, ValidationKeys.ToCamelCase(propertyPath));
    }
}
