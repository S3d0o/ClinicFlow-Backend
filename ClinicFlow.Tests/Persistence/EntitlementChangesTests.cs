using Domain.Entities.AppModule;
using Domain.Entities.Subscriptions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Persistence.AppData;
using Persistence.Subscriptions;
using Persistence.Tenancy;
using Services.Abstraction.Contracts;

namespace ClinicFlow.Tests.Persistence
{
    // Which cached entitlements a save makes stale (docs/v2/adr/0008 decision 7). Reads the change tracker only, so
    // no database is needed
    public class EntitlementChangesTests
    {
        private static ClinicDbContext NewContext() => new(
            new DbContextOptionsBuilder<ClinicDbContext>().UseSqlServer("Server=unused;Database=ClinicFlowV2").Options,
            new SingleOrganizationTenantContext());

        private readonly IEntitlementCache _cache = Substitute.For<IEntitlementCache>();

        [Fact]
        public void ASubscriptionOrOverrideChange_StalesItsOrganization()
        {
            using var db = NewContext();
            db.Attach(new OrganizationSubscription { OrganizationId = 1, PlanId = 2 }).State = EntityState.Modified;
            db.Attach(new OrganizationModuleOverride { OrganizationId = 4, ModuleCode = "REC", Enabled = true }).State = EntityState.Deleted;
            var changes = new EntitlementChanges();

            changes.CollectFrom(db.ChangeTracker);
            changes.ApplyTo(_cache);

            Assert.Equal([1, 4], changes.Organizations.Order());
            _cache.Received(1).Invalidate(1);
            _cache.Received(1).Invalidate(4);
            _cache.DidNotReceive().InvalidateAll();
        }

        [Fact]
        public void APlanChange_StalesEveryOrganization()
        {
            using var db = NewContext();
            db.Attach(new PlanLimit { PlanId = 1, LimitKey = "doctors", Value = 3 }).State = EntityState.Modified;
            var changes = new EntitlementChanges();

            changes.CollectFrom(db.ChangeTracker);
            changes.ApplyTo(_cache);

            Assert.True(changes.AllOrganizations);
            _cache.Received(1).InvalidateAll();
        }

        [Fact]
        public void UnchangedOrOtherRows_StaleNothing()
        {
            using var db = NewContext();
            db.Attach(new OrganizationSubscription { OrganizationId = 1, PlanId = 2 }); // Unchanged
            db.Attach(new Specialty { Id = 9, Name = "Cardiology" }).State = EntityState.Modified;
            var changes = new EntitlementChanges();

            changes.CollectFrom(db.ChangeTracker);

            Assert.True(changes.IsEmpty);
        }

        [Fact]
        public void Clear_ForgetsWhatWasCollected()
        {
            using var db = NewContext();
            db.Attach(new OrganizationSubscription { OrganizationId = 1, PlanId = 2 }).State = EntityState.Modified;
            var changes = new EntitlementChanges();
            changes.CollectFrom(db.ChangeTracker);

            changes.Clear();

            Assert.True(changes.IsEmpty);
        }
    }
}
