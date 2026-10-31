using Domain.Entities.AppModule;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Persistence.AppData.Converters;
using Domain.Entities.Subscriptions;
using Persistence.Outbox;
using Persistence.Subscriptions;
using Persistence.Tenancy;
using Services.Abstraction.Contracts;
using System.Diagnostics;
using System.Linq.Expressions;

namespace Persistence.AppData
{
    public class ClinicDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser? _currentUser;
        private string? _auditCorrelationId;

        // currentUser is who audit entries name; without one (tools, model tests) they name nobody. entitlementCache is
        // invalidated when a save changes subscriptions, overrides or plans; without one (tools, tests' own contexts)
        // nothing is cached in this process to invalidate
        public ClinicDbContext(
            DbContextOptions<ClinicDbContext> options, ITenantContext tenant, ICurrentUser? currentUser = null,
            IEntitlementCache? entitlementCache = null)
            : base(options)
        {
            _tenant = tenant;
            _currentUser = currentUser;
            EntitlementCache = entitlementCache;
        }

        // The organization this context reads and writes. The query filters read it per context instance
        public int OrganizationId => _tenant.OrganizationId;

        // Whether entity events are written to the outbox on save. The demo seeder turns it off: seeded history
        // must not send notifications or emails
        public bool RecordDomainEvents { get; set; } = true;

        // Whether changes to audited types write audit entries (FR-AUD-01). The demo seeder and test seeding turn it
        // off: stored history isn't an action anyone took
        public bool RecordAudit { get; set; } = true;

        // Who and where audit entries name: no user outside a signed-in request (registration, background jobs)
        internal (Guid? UserId, string? IpAddress) AuditActor => _currentUser is { IsAuthenticated: true } user
            ? (user.UserId, user.IpAddress)
            : (null, _currentUser?.IpAddress);

        // Shared by every entry this context writes, so a request's entries group together (a context lives for one
        // request or one job run). The trace id, when there is one, links them to the request's logs
        internal string AuditCorrelationId
            => _auditCorrelationId ??= Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");

        // The cache of effective entitlements (docs/v2/adr/0008 decision 7) and what the saves of this context have
        // made stale in it, applied after each save and again on commit (EntitlementChanges)
        internal IEntitlementCache? EntitlementCache { get; }
        internal EntitlementChanges PendingEntitlementChanges { get; } = new();

        // Rows inserted by the save in progress whose audit entries wait for their database-generated keys
        internal List<EntityEntry> PendingAuditInserts { get; } = [];

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            // Order matters: the outbox and the audit add their rows first, so the tenant interceptor stamps them too.
            // The entitlement interceptors run last: they only read what the save changed
            => optionsBuilder.AddInterceptors(
                OutboxSaveChangesInterceptor.Instance, AuditSaveChangesInterceptor.Instance, TenantSaveChangesInterceptor.Instance,
                EntitlementSaveChangesInterceptor.Instance, EntitlementTransactionInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClinicDbContext).Assembly);
            modelBuilder.Entity<ApplicationUser>().ToTable("Users");
            modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles");
            modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles").IsAudited("UserRole"); // role changes
            modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
            modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
            modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
            modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

            ConfigureTenancy(modelBuilder);
        }

        // Every organization-owned entity gets its foreign keys and a filter to the current organization, so no query
        // can see another clinic's rows (docs/v2/adr/0001). Done here, by interface, so a new entity can't forget it
        private void ConfigureTenancy(ModelBuilder modelBuilder)
        {
            var tenantOwned = modelBuilder.Model.GetEntityTypes()
                .Select(t => t.ClrType)
                .Where(t => typeof(IOrganizationOwned).IsAssignableFrom(t) || typeof(IBranchOwned).IsAssignableFrom(t))
                .ToList();

            foreach (var clrType in tenantOwned)
            {
                var entity = modelBuilder.Entity(clrType);

                if (typeof(IOrganizationOwned).IsAssignableFrom(clrType))
                {
                    entity.HasOne(typeof(Organization)).WithMany()
                        .HasForeignKey(nameof(IOrganizationOwned.OrganizationId))
                        .OnDelete(DeleteBehavior.Restrict);

                    // e => e.OrganizationId == this.OrganizationId
                    var e = Expression.Parameter(clrType, "e");
                    var filter = Expression.Lambda(
                        Expression.Equal(
                            Expression.Property(e, nameof(IOrganizationOwned.OrganizationId)),
                            Expression.Property(Expression.Constant(this), nameof(OrganizationId))),
                        e);
                    entity.HasQueryFilter(filter);
                }

                if (typeof(IBranchOwned).IsAssignableFrom(clrType))
                {
                    entity.HasOne(typeof(Branch)).WithMany()
                        .HasForeignKey(nameof(IBranchOwned.BranchId))
                        .OnDelete(DeleteBehavior.Restrict);
                }
            }
        }

        // Covers DateTime? properties too
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        public DbSet<Organization> Organizations => Set<Organization>();
        public DbSet<Branch> Branches => Set<Branch>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<BranchWorkingHours> BranchWorkingHours => Set<BranchWorkingHours>();
        public DbSet<BranchHoliday> Holidays => Set<BranchHoliday>();
        public DbSet<DoctorProfile> DoctorProfiles => Set<DoctorProfile>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
        public DbSet<AppointmentSlot> AppointmentSlots => Set<AppointmentSlot>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<MessageDelivery> MessageDeliveries => Set<MessageDelivery>();
        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
        public DbSet<Plan> Plans => Set<Plan>();
        public DbSet<PlanModule> PlanModules => Set<PlanModule>();
        public DbSet<PlanLimit> PlanLimits => Set<PlanLimit>();
        public DbSet<OrganizationSubscription> OrganizationSubscriptions => Set<OrganizationSubscription>();
        public DbSet<OrganizationModuleOverride> OrganizationModuleOverrides => Set<OrganizationModuleOverride>();

    }
}
