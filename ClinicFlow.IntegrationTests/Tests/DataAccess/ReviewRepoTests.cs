using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Persistence.AppData;
using Persistence.Repositories;

namespace ClinicFlow.IntegrationTests.Tests.DataAccess
{
    // ReviewRepo.TryAddAsync against the real unique index on Reviews.AppointmentId (formerly a catch in ReviewService)
    [Collection(nameof(IntegrationTestCollection))]
    public class ReviewRepoTests : IAsyncLifetime
    {
        private readonly ClinicFlowApiFactory _factory;
        private int _appointmentId;
        private int _patientProfileId;
        private int _doctorProfileId;

        public ReviewRepoTests(ClinicFlowApiFactory factory) => _factory = factory;

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            await _factory.SeedAsync(async db =>
            {
                (_, _, _, _appointmentId) = await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Completed);
                var appointment = await db.Appointments.SingleAsync(a => a.Id == _appointmentId);
                (_patientProfileId, _doctorProfileId) = (appointment.PatientProfileId, appointment.DoctorProfileId);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task TryAddAsync_WhenTheAppointmentHasNoReview_SavesItAndReturnsTrue()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

            var added = await new ReviewRepo(db).TryAddAsync(NewReview(_doctorProfileId), CancellationToken.None);

            added.Should().BeTrue();
            (await db.Reviews.CountAsync(r => r.AppointmentId == _appointmentId)).Should().Be(1);
        }

        [Fact]
        public async Task TryAddAsync_WhenAConcurrentRequestReviewedFirst_ReturnsFalseAndForgetsTheReview()
        {
            await _factory.SeedAsync(async db =>
            {
                db.Reviews.Add(NewReview(_doctorProfileId));
                await db.SaveChangesAsync();
            });
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            var duplicate = NewReview(_doctorProfileId);

            var added = await new ReviewRepo(db).TryAddAsync(duplicate, CancellationToken.None);

            added.Should().BeFalse();
            db.Entry(duplicate).State.Should().Be(EntityState.Detached);
            (await db.Reviews.CountAsync(r => r.AppointmentId == _appointmentId)).Should().Be(1);
        }

        [Fact]
        public async Task TryAddAsync_WhenTheInsertFailsForAnotherReason_Throws()
        {
            // No review exists for the appointment, so the failure (here an unknown doctor) isn't a duplicate
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

            var act = () => new ReviewRepo(db).TryAddAsync(NewReview(doctorProfileId: 99_999), CancellationToken.None);

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        private Review NewReview(int doctorProfileId) => new()
        {
            AppointmentId = _appointmentId,
            PatientProfileId = _patientProfileId,
            DoctorProfileId = doctorProfileId,
            Rating = 4,
            IsVisible = true,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
