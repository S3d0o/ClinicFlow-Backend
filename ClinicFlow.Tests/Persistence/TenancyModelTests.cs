using Domain.Entities.AppModule;
using Domain.Entities.Auditing;
using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Entities.Subscriptions;
using Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.AppData;
using Persistence.Tenancy;

namespace ClinicFlow.Tests.Persistence
{
    // Every organization-owned table must be filtered to the current organization, or a query leaks another clinic's
    // rows (docs/v2/adr/0001). These read the EF model; building it needs no database
    public class TenancyModelTests
    {
        private static readonly IModel Model = new ClinicDbContext(
            new DbContextOptionsBuilder<ClinicDbContext>().UseSqlServer("Server=unused;Database=ClinicFlowV2").Options,
            new SingleOrganizationTenantContext()).Model;

        private static readonly Type[] ExpectedOrganizationOwned =
            [typeof(Branch), typeof(DoctorProfile), typeof(DoctorSchedule), typeof(AppointmentSlot), typeof(Appointment), typeof(Review), typeof(Notification),
             typeof(OutboxMessage), typeof(MessageDelivery), // an event belongs to the organization whose change raised it
             typeof(AuditEntry),
             typeof(OrganizationSubscription), typeof(OrganizationModuleOverride), // an organization's plan and add-ons (docs/v2/adr/0008)
             typeof(Room), typeof(BranchWorkingHours), typeof(BranchHoliday)]; // FR-ORG-03, P1 PR 2

        private static readonly Type[] ExpectedBranchOwned =
            [typeof(DoctorSchedule), typeof(AppointmentSlot), typeof(Appointment), typeof(Room), typeof(BranchWorkingHours)];

        public static TheoryData<string> EntityTypes => [.. Model.GetEntityTypes().Select(t => t.Name)];

        [Theory]
        [MemberData(nameof(EntityTypes))]
        public void EntityWithOrganizationId_IsOrganizationOwnedAndFiltered(string entityTypeName)
        {
            var entityType = Model.FindEntityType(entityTypeName)!;
            if (entityType.FindProperty(nameof(IOrganizationOwned.OrganizationId)) is null || entityType.ClrType == typeof(Organization))
                return;

            // A column named OrganizationId without the interface would get neither the filter nor the stamping
            Assert.True(typeof(IOrganizationOwned).IsAssignableFrom(entityType.ClrType),
                $"{entityType.ClrType.Name} has OrganizationId but doesn't implement IOrganizationOwned");
            Assert.NotEmpty(entityType.GetDeclaredQueryFilters());
        }

        [Fact]
        public void OrganizationOwnedTypes_AreTheExpectedOnes()
        {
            var owned = Model.GetEntityTypes()
                .Where(t => typeof(IOrganizationOwned).IsAssignableFrom(t.ClrType))
                .Select(t => t.ClrType);

            Assert.Equal(ExpectedOrganizationOwned.Select(t => t.Name).Order(), owned.Select(t => t.Name).Order());
        }

        [Fact]
        public void OrganizationOwnedTypes_HaveAFilterAndARequiredForeignKeyToOrganization()
        {
            foreach (var type in ExpectedOrganizationOwned)
            {
                var entityType = Model.FindEntityType(type)!;
                Assert.NotEmpty(entityType.GetDeclaredQueryFilters());
                Assert.Contains(entityType.GetForeignKeys(), fk =>
                    fk.PrincipalEntityType.ClrType == typeof(Organization)
                    && fk.IsRequired
                    && fk.DeleteBehavior == DeleteBehavior.Restrict
                    && fk.Properties.Single().Name == nameof(IOrganizationOwned.OrganizationId));
            }
        }

        [Fact]
        public void BranchOwnedTypes_HaveARequiredForeignKeyToBranch()
        {
            var branchOwned = Model.GetEntityTypes()
                .Where(t => typeof(IBranchOwned).IsAssignableFrom(t.ClrType))
                .Select(t => t.ClrType.Name);
            Assert.Equal(ExpectedBranchOwned.Select(t => t.Name).Order(), branchOwned.Order());

            foreach (var type in ExpectedBranchOwned)
                Assert.Contains(Model.FindEntityType(type)!.GetForeignKeys(), fk =>
                    fk.PrincipalEntityType.ClrType == typeof(Branch) && fk.IsRequired && fk.DeleteBehavior == DeleteBehavior.Restrict);
        }

        [Theory]
        [InlineData(typeof(Organization))]
        [InlineData(typeof(ApplicationUser))]
        [InlineData(typeof(PatientProfile))]
        [InlineData(typeof(Specialty))]
        [InlineData(typeof(RefreshToken))]
        [InlineData(typeof(Plan))]       // plans are platform data, sold to every organization (docs/v2/adr/0008 decision 6)
        [InlineData(typeof(PlanModule))]
        [InlineData(typeof(PlanLimit))]
        public void GlobalTypes_AreNotFiltered(Type type)
            // People, identity and reference data are shared across organizations (docs/v2/adr/0001 §3)
            => Assert.Empty(Model.FindEntityType(type)!.GetDeclaredQueryFilters());

        [Fact]
        public void BranchCode_IsUniquePerOrganization()
            => Assert.Contains(Model.FindEntityType(typeof(Branch))!.GetIndexes(), i =>
                i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(Branch.OrganizationId), nameof(Branch.Code)]));
    }
}
