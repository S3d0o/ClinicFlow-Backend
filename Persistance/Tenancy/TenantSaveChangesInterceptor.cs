using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Persistence.Tenancy
{
    // Fills in OrganizationId on new organization-owned rows and refuses to save a row that belongs to another
    // organization, so a missing or wrong id can't leak data between clinics (docs/v2/adr/0001). The organization comes
    // from the context being saved. ExecuteUpdate/ExecuteDelete skip interceptors but are still filtered by the
    // context's query filters.
    public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
    {
        public static readonly TenantSaveChangesInterceptor Instance = new();

        private TenantSaveChangesInterceptor() { }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Stamp(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Stamp(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void Stamp(DbContext? context)
        {
            if (context is not ClinicDbContext clinic)
                return;

            var organizationId = clinic.OrganizationId;
            foreach (var entry in clinic.ChangeTracker.Entries<IOrganizationOwned>())
            {
                if (entry.State == EntityState.Added && entry.Entity.OrganizationId == 0)
                    entry.Entity.OrganizationId = organizationId;

                if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity.OrganizationId != organizationId)
                    throw new InvalidOperationException(
                        $"{entry.Metadata.ClrType.Name} belongs to organization {entry.Entity.OrganizationId}, " +
                        $"but this context saves for organization {organizationId}.");
            }
        }
    }
}
