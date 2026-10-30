using Domain.Entities.Subscriptions;

namespace Persistence.AppData.Configurations
{
    // Plans are platform-owned data (docs/v2/adr/0008 decisions 3 and 6): no organization, no query filter. The three
    // plans below are the ADR's example packaging, seeded by the AddEntitlements migration; prices and changes come
    // with the platform console (R4). Code must never branch on these ids or codes
    internal class PlanConfig : IEntityTypeConfiguration<Plan>
    {
        public void Configure(EntityTypeBuilder<Plan> builder)
        {
            builder.ToTable("Plans");
            builder.HasIndex(p => p.Code).IsUnique();
            builder.Property(p => p.Code).HasMaxLength(32);
            builder.Property(p => p.Name).HasMaxLength(100);

            builder.HasMany(p => p.Modules).WithOne().HasForeignKey(m => m.PlanId).OnDelete(DeleteBehavior.Cascade);
            builder.HasMany(p => p.Limits).WithOne().HasForeignKey(l => l.PlanId).OnDelete(DeleteBehavior.Cascade);

            builder.HasData(PlanSeed.Plans);
        }
    }

    internal class PlanModuleConfig : IEntityTypeConfiguration<PlanModule>
    {
        public void Configure(EntityTypeBuilder<PlanModule> builder)
        {
            builder.ToTable("PlanModules");
            builder.HasKey(m => new { m.PlanId, m.ModuleCode });
            builder.Property(m => m.ModuleCode).HasMaxLength(10);

            builder.HasData(PlanSeed.PlanModules);
        }
    }

    internal class PlanLimitConfig : IEntityTypeConfiguration<PlanLimit>
    {
        public void Configure(EntityTypeBuilder<PlanLimit> builder)
        {
            builder.ToTable("PlanLimits");
            builder.HasKey(l => new { l.PlanId, l.LimitKey });
            builder.Property(l => l.LimitKey).HasMaxLength(64);

            builder.HasData(PlanSeed.PlanLimits);
        }
    }

    // The seeded plans (ADR 0008, "Initial packaging"; the frontend's mock has the same in src/mocks/seed/subscription.ts)
    internal static class PlanSeed
    {
        public const int StarterId = 1;
        public const int ClinicId = 2;
        public const int FullId = 3;

        public static readonly Plan[] Plans =
        [
            new() { Id = StarterId, Code = "Starter", Name = "Starter", IsPublic = true },
            new() { Id = ClinicId, Code = "Clinic", Name = "Clinic", IsPublic = true },
            new() { Id = FullId, Code = "Full", Name = "Full", IsPublic = true },
        ];

        private static readonly Dictionary<int, string[]> AddOns = new()
        {
            [StarterId] = [],
            [ClinicId] = [Modules.FrontDesk, Modules.Billing, Modules.PatientPortal, Modules.Reporting],
            [FullId] = [Modules.FrontDesk, Modules.Billing, Modules.PatientPortal, Modules.Reporting, Modules.ClinicalRecords, Modules.Insurance],
        };

        // Branches, doctors, phone messages per month
        private static readonly Dictionary<int, (int Branches, int Doctors, int PhoneMessages)> Sizes = new()
        {
            [StarterId] = (1, 2, 300),
            [ClinicId] = (1, 5, 1000),
            [FullId] = (3, 15, 2500),
        };

        public static readonly PlanModule[] PlanModules = AddOns
            .SelectMany(p => p.Value.Select(m => new PlanModule { PlanId = p.Key, ModuleCode = m }))
            .ToArray();

        public static readonly PlanLimit[] PlanLimits = Sizes
            .SelectMany(p => new[]
            {
                new PlanLimit { PlanId = p.Key, LimitKey = Limits.Branches, Value = p.Value.Branches },
                new PlanLimit { PlanId = p.Key, LimitKey = Limits.Doctors, Value = p.Value.Doctors },
                new PlanLimit { PlanId = p.Key, LimitKey = Limits.PhoneMessagesPerMonth, Value = p.Value.PhoneMessages },
            })
            .ToArray();
    }
}
