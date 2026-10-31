using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Entities.Tenancy;
using Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Persistence.AppData;
using Shared.Enums;

namespace ClinicFlow.IntegrationTests.Tests.DataAccess
{
    // AddStaffMembers backfills who can do what (P1 PR 4): every Admin and Doctor user becomes an Active staff member,
    // Admins organization-wide and Doctors at MAIN, each doctor profile points to its staff member, and patients
    // stay as they are. Run against rows written under the previous schema
    [Collection(nameof(IntegrationTestCollection))]
    public class AddStaffMembersTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string PreviousMigration = "20261031011003_AddOrganizationSettings";

        private readonly Guid _adminId = Guid.NewGuid();
        private readonly Guid _doctorId = Guid.NewGuid();
        private readonly Guid _patientId = Guid.NewGuid();
        private readonly Guid _adminDoctorId = Guid.NewGuid();

        public async Task InitializeAsync()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Migrate_AdminAndDoctorUsers_BecomeStaffMembers()
        {
            // Users, roles and specialties have the same columns before and after; doctor profiles don't, so SQL
            await factory.SeedAsync(async db =>
            {
                await SeedDataHelper.SeedRolesAsync(db);
                var specialty = db.Specialties.Add(new Specialty { Name = "Cardiology", IsActive = true }).Entity;
                foreach (var (id, name) in new[] { (_adminId, "admin"), (_doctorId, "doctor"), (_patientId, "patient"), (_adminDoctorId, "both") })
                    db.Users.Add(new ApplicationUser
                    {
                        Id = id, UserName = $"{name}@test.com", NormalizedUserName = $"{name}@TEST.COM".ToUpperInvariant(),
                        Email = $"{name}@test.com", FirstName = name, LastName = "Test", Gender = Gender.Female, IsActive = true,
                        SecurityStamp = Guid.NewGuid().ToString(),
                    });
                await db.SaveChangesAsync();

                var roles = await db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);
                db.UserRoles.AddRange(
                    new IdentityUserRole<Guid> { UserId = _adminId, RoleId = roles["Admin"] },
                    new IdentityUserRole<Guid> { UserId = _doctorId, RoleId = roles["Doctor"] },
                    new IdentityUserRole<Guid> { UserId = _patientId, RoleId = roles["Patient"] },
                    new IdentityUserRole<Guid> { UserId = _adminDoctorId, RoleId = roles["Admin"] },
                    new IdentityUserRole<Guid> { UserId = _adminDoctorId, RoleId = roles["Doctor"] });
                await db.SaveChangesAsync();

                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO Doctors (OrganizationId, UserId, SpecialtyId, YearsOfExperience, ConsultationFee, AverageRating, TotalReviews, IsApprovedByAdmin)
                    VALUES (1, {_doctorId}, {specialty.Id}, 5, 300, 0, 0, 1)
                    """);
            });

            await factory.SeedAsync(db => db.GetService<IMigrator>().MigrateAsync());

            await factory.SeedAsync(async db =>
            {
                var staff = await db.StaffMembers.AsNoTracking().Include(s => s.Assignments).ToListAsync();
                staff.Select(s => s.UserId).Should().BeEquivalentTo([_adminId, _doctorId, _adminDoctorId]);
                staff.Should().OnlyContain(s => s.Status == StaffStatus.Active && s.OrganizationId == 1);

                staff.Single(s => s.UserId == _adminId).Assignments
                    .Should().ContainSingle(a => a.Role == "Admin" && a.BranchId == null);
                staff.Single(s => s.UserId == _doctorId).Assignments
                    .Should().ContainSingle(a => a.Role == "Doctor" && a.BranchId == Branch.MainId);
                staff.Single(s => s.UserId == _adminDoctorId).Assignments.Select(a => (a.Role, a.BranchId))
                    .Should().BeEquivalentTo([("Admin", (int?)null), ("Doctor", (int?)Branch.MainId)]);

                var doctor = await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == _doctorId);
                doctor.StaffMemberId.Should().Be(staff.Single(s => s.UserId == _doctorId).Id);
            });
        }
    }
}
