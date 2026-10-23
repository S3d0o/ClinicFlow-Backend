using Domain.Entities.AppModule;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Persistence.AppData.Converters;
using Persistence.Outbox;
using Persistence.Tenancy;
using System.Linq.Expressions;

namespace Persistence.AppData
{
    public class ClinicDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        private readonly ITenantContext _tenant;

        public ClinicDbContext(DbContextOptions<ClinicDbContext> options, ITenantContext tenant)
            : base(options)
        {
            _tenant = tenant;
        }

        // The organization this context reads and writes. The query filters read it per context instance
        public int OrganizationId => _tenant.OrganizationId;

        // Whether entity events are written to the outbox on save. The demo seeder turns it off: seeded history
        // must not send notifications or emails
        public bool RecordDomainEvents { get; set; } = true;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            // Order matters: the outbox adds its rows first, so the tenant interceptor stamps them too
            => optionsBuilder.AddInterceptors(OutboxSaveChangesInterceptor.Instance, TenantSaveChangesInterceptor.Instance);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClinicDbContext).Assembly);
            modelBuilder.Entity<ApplicationUser>().ToTable("Users");
            modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles");
            modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
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

    }
}
