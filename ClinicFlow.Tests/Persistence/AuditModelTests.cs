using Domain.Entities.Tenancy;
using Domain.Entities.AppModule;
using Domain.Entities.Auditing;
using Domain.Entities.IdentityModule;
using Domain.Entities.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.AppData;
using Persistence.Auditing;
using Persistence.Tenancy;

namespace ClinicFlow.Tests.Persistence
{
    // What the audit interceptor records (FR-AUD-01, P0 PR 10): the audited types and how each property is written.
    // These read the runtime EF model, so they also prove the audit annotations survive into it
    public class AuditModelTests
    {
        private static readonly IModel Model = new ClinicDbContext(
            new DbContextOptionsBuilder<ClinicDbContext>().UseSqlServer("Server=unused;Database=ClinicFlowV2").Options,
            new SingleOrganizationTenantContext()).Model;

        private static AuditedType? Audited<T>() => AuditedType.Of(Model.FindEntityType(typeof(T))!);

        private static AuditMode ModeOf<T>(string property)
            => Audited<T>()!.ModeOf(Model.FindEntityType(typeof(T))!.FindProperty(property)!);

        [Fact]
        public void AuditedTypes_AreAppointmentsDoctorsUsersRolesSubscriptionsAndTheOrganizationsSetup()
        {
            var audited = Model.GetEntityTypes()
                .Select(t => (t.ClrType, Audited: AuditedType.Of(t)))
                .Where(t => t.Audited is not null)
                .ToDictionary(t => t.ClrType, t => t.Audited!.Name);

            Assert.Equal(new Dictionary<Type, string>
            {
                [typeof(Appointment)] = "Appointment",
                [typeof(DoctorProfile)] = "DoctorProfile",
                [typeof(ApplicationUser)] = "User",
                [typeof(IdentityUserRole<Guid>)] = "UserRole",
                [typeof(OrganizationSubscription)] = "OrganizationSubscription",     // docs/v2/adr/0008 decision 6
                [typeof(OrganizationModuleOverride)] = "OrganizationModuleOverride",
                [typeof(Organization)] = "Organization",                             // FR-ORG-01, P1 PR 1
                [typeof(Branch)] = "Branch",
                [typeof(Room)] = "Room",
                [typeof(BranchWorkingHours)] = "BranchWorkingHours",                 // FR-ORG-03, P1 PR 2
                [typeof(BranchHoliday)] = "Holiday",
            }, audited);
        }

        [Theory]
        [InlineData(nameof(OrganizationSubscription.PlanId), AuditMode.Recorded)]
        [InlineData(nameof(OrganizationSubscription.Status), AuditMode.Recorded)]
        [InlineData(nameof(OrganizationSubscription.CurrentPeriodEnd), AuditMode.Recorded)]
        [InlineData(nameof(OrganizationSubscription.OrganizationId), AuditMode.Ignored)] // the entry's id and its own
        public void OrganizationSubscription_RecordsThePlanAndStatus(string property, AuditMode expected)
            => Assert.Equal(expected, ModeOf<OrganizationSubscription>(property));

        [Theory]
        [InlineData(nameof(OrganizationModuleOverride.ModuleCode), AuditMode.Recorded)]
        [InlineData(nameof(OrganizationModuleOverride.Enabled), AuditMode.Recorded)]
        public void OrganizationModuleOverride_RecordsTheModuleAndSwitch(string property, AuditMode expected)
            => Assert.Equal(expected, ModeOf<OrganizationModuleOverride>(property));

        [Fact]
        public void Plans_AreNotAudited()
        {
            // Platform data, changed only by migrations until the operator console (R4); an audit entry belongs to an
            // organization, so the console's plan changes need a platform audit trail of their own
            Assert.Null(Audited<Plan>());
            Assert.Null(Audited<PlanModule>());
            Assert.Null(Audited<PlanLimit>());
        }

        [Fact]
        public void AuditEntry_IsNotItselfAudited()
            => Assert.Null(Audited<AuditEntry>());

        [Theory]
        [InlineData(nameof(Appointment.DoctorNotes), AuditMode.Redacted)]
        [InlineData(nameof(Appointment.ReasonForVisit), AuditMode.Redacted)]
        [InlineData(nameof(Appointment.ReminderSentAt), AuditMode.Ignored)]
        [InlineData(nameof(Appointment.Status), AuditMode.Recorded)]
        [InlineData(nameof(Appointment.CancellationReason), AuditMode.Recorded)]
        [InlineData(nameof(Appointment.Id), AuditMode.Ignored)]             // the entry's EntityId
        [InlineData(nameof(Appointment.OrganizationId), AuditMode.Ignored)] // the entry's own
        public void Appointment_RecordsClinicalTextOnlyAsChanged(string property, AuditMode expected)
            => Assert.Equal(expected, ModeOf<Appointment>(property));

        [Theory]
        [InlineData(nameof(DoctorProfile.IsApprovedByAdmin), AuditMode.Recorded)]
        [InlineData(nameof(DoctorProfile.ConsultationFee), AuditMode.Recorded)]
        [InlineData(nameof(DoctorProfile.AverageRating), AuditMode.Ignored)]
        [InlineData(nameof(DoctorProfile.TotalReviews), AuditMode.Ignored)]
        public void DoctorProfile_RecordsApprovalAndProfile_NotTheRatingCache(string property, AuditMode expected)
            => Assert.Equal(expected, ModeOf<DoctorProfile>(property));

        [Fact]
        public void User_RecordsOnlySuspension()
        {
            var user = Model.FindEntityType(typeof(ApplicationUser))!;

            var recorded = user.GetProperties()
                .Where(p => Audited<ApplicationUser>()!.ModeOf(p) != AuditMode.Ignored)
                .Select(p => p.Name);

            Assert.Equal([nameof(ApplicationUser.IsActive)], recorded);
        }

        [Fact]
        public void UserRole_RecordsBothKeys()
        {
            // A composite key is what the record is, so it is recorded, unlike a single generated id
            Assert.Equal(AuditMode.Recorded, ModeOf<IdentityUserRole<Guid>>(nameof(IdentityUserRole<Guid>.UserId)));
            Assert.Equal(AuditMode.Recorded, ModeOf<IdentityUserRole<Guid>>(nameof(IdentityUserRole<Guid>.RoleId)));
        }
    }
}
