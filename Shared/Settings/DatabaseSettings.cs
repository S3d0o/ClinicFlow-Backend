using System.Data.Common;

namespace Shared.Settings
{
    public class DatabaseSettings
    {
        // V2 runs on its own database (docs/v2/adr/0007-v2-separate-product-line.md). Starting against any
        // other database, such as V1's ClinicFlow, would migrate it, so startup refuses (InfraStructureServiceExtensions)
        public string ExpectedName { get; init; } = "ClinicFlowV2";

        public bool Allows(string connectionString)
            => string.Equals(DatabaseName(connectionString), ExpectedName, StringComparison.OrdinalIgnoreCase);

        // The database a connection string names (Database or Initial Catalog), or null when it names none
        public static string? DatabaseName(string connectionString)
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            foreach (var key in new[] { "Database", "Initial Catalog" })
            {
                if (builder.TryGetValue(key, out var value) && value is string name && !string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return null;
        }
    }
}
