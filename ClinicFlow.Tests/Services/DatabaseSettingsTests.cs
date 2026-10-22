using Shared.Settings;

namespace ClinicFlow.Tests.Services
{
    // docs/v2/adr/0007: V2 refuses to start against any database but its own, so it can never migrate V1's
    public class DatabaseSettingsTests
    {
        private readonly DatabaseSettings _settings = new();

        [Fact]
        public void ExpectedName_ByDefault_IsTheV2Database()
        {
            Assert.Equal("ClinicFlowV2", _settings.ExpectedName);
        }

        [Theory]
        [InlineData("Server=localhost,1433;Database=ClinicFlowV2;User Id=sa;Password=x;TrustServerCertificate=True")]
        [InlineData("Server=localhost;Initial Catalog=ClinicFlowV2;Integrated Security=True")]
        [InlineData("Server=localhost;database=clinicflowv2")]
        public void Allows_WhenTheConnectionStringNamesTheV2Database_ReturnsTrue(string connectionString)
        {
            Assert.True(_settings.Allows(connectionString));
        }

        [Theory]
        [InlineData("Server=localhost,1433;Database=ClinicFlow;User Id=sa;Password=x;TrustServerCertificate=True")] // V1's database
        [InlineData("Server=localhost;Initial Catalog=ClinicFlow")]
        [InlineData("Server=localhost;Database=ClinicFlowV2Old")]
        [InlineData("Server=localhost;User Id=sa;Password=x")] // no database: SQL Server would use the login's default
        public void Allows_WhenTheConnectionStringNamesAnotherDatabaseOrNone_ReturnsFalse(string connectionString)
        {
            Assert.False(_settings.Allows(connectionString));
        }

        [Fact]
        public void Allows_WhenExpectedNameIsConfigured_ComparesWithIt()
        {
            var settings = new DatabaseSettings { ExpectedName = "ClinicFlowV2TestDb" };

            Assert.True(settings.Allows("Server=localhost;Database=ClinicFlowV2TestDb"));
            Assert.False(settings.Allows("Server=localhost;Database=ClinicFlowV2"));
        }

        [Fact]
        public void DatabaseName_WhenNoneIsNamed_ReturnsNull()
        {
            Assert.Null(DatabaseSettings.DatabaseName("Server=localhost;User Id=sa;Password=x"));
        }
    }
}
