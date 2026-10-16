using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Review;
using System.Net;
using System.Net.Http.Json;

namespace ClinicFlow.IntegrationTests.Tests.Reviews
{
    [Collection(nameof(IntegrationTestCollection))]
    public class SubmitReviewTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/reviews";

        private Guid _patientUserId;
        private Guid _doctorUserId;
        private int _completedAppointmentId;
        private Guid _otherPatientUserId;
        private int _otherCompletedAppointmentId;
        private int _confirmedAppointmentId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public SubmitReviewTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();

            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                (_patientUserId, _doctorUserId, _, _completedAppointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Completed);

                (_otherPatientUserId, _, _, _otherCompletedAppointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Completed, "2");

                (_, _, _, _confirmedAppointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Confirmed, "3");
            });
        }

        [Fact]
        public async Task SubmitReview_CompletedAppointment_Returns201AndUpdatesDoctorRating()
        {
            AuthenticateAs(_patientUserId, "Patient");

            var response = await _client.PostAsJsonAsync(BaseUrl, new SubmitReviewRequest(_completedAppointmentId, 4, "Very thorough"));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await response.Content.ReadFromJsonAsync<ReviewResponse>();
            body!.Id.Should().BePositive();
            body.Rating.Should().Be(4);
            body.Comment.Should().Be("Very thorough");
            body.PatientName.Should().Be("Test Patient");

            await _factory.SeedAsync(async db =>
            {
                var doctor = await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == _doctorUserId);
                doctor.AverageRating.Should().Be(4);
                doctor.TotalReviews.Should().Be(1);
            });
        }

        [Fact]
        public async Task SubmitReview_AlreadyReviewed_Returns409()
        {
            AuthenticateAs(_patientUserId, "Patient");
            var request = new SubmitReviewRequest(_completedAppointmentId, 5, null);
            (await _client.PostAsJsonAsync(BaseUrl, request)).StatusCode.Should().Be(HttpStatusCode.Created);

            var response = await _client.PostAsJsonAsync(BaseUrl, request);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.ReviewAlreadyExists");
        }

        [Fact]
        public async Task SubmitReview_ConcurrentDuplicates_CreatesOneReviewAndRejectsTheRestWith409()
        {
            // Whichever way the requests interleave, the pre-check or the unique index must reject
            // every duplicate with 409; none may surface as a 500 (REVIEW.md L8)
            AuthenticateAs(_patientUserId, "Patient");
            var request = new SubmitReviewRequest(_completedAppointmentId, 3, "race");

            var responses = await Task.WhenAll(
                Enumerable.Range(0, 5).Select(_ => _client.PostAsJsonAsync(BaseUrl, request)));

            responses.Select(r => r.StatusCode).Should()
                .ContainSingle(s => s == HttpStatusCode.Created)
                .And.AllSatisfy(s => s.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict));

            await _factory.SeedAsync(async db =>
            {
                (await db.Reviews.CountAsync(r => r.AppointmentId == _completedAppointmentId)).Should().Be(1);
                var doctor = await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == _doctorUserId);
                doctor.TotalReviews.Should().Be(1);
            });
        }

        [Fact]
        public async Task SubmitReview_OtherPatientsAppointment_Returns403()
        {
            AuthenticateAs(_patientUserId, "Patient");

            var response = await _client.PostAsJsonAsync(BaseUrl, new SubmitReviewRequest(_otherCompletedAppointmentId, 1, "not mine"));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await _factory.SeedAsync(async db =>
                (await db.Reviews.AnyAsync()).Should().BeFalse());
        }

        [Fact]
        public async Task SubmitReview_OwnAppointmentNotCompleted_Returns400()
        {
            int ownConfirmedAppointmentId = 0;
            await _factory.SeedAsync(async db =>
            {
                var completed = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _completedAppointmentId);
                var confirmed = await db.Appointments.SingleAsync(a => a.Id == _confirmedAppointmentId);
                confirmed.PatientProfileId = completed.PatientProfileId;
                await db.SaveChangesAsync();
                ownConfirmedAppointmentId = confirmed.Id;
            });
            AuthenticateAs(_patientUserId, "Patient");

            var response = await _client.PostAsJsonAsync(BaseUrl, new SubmitReviewRequest(ownConfirmedAppointmentId, 5, null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.ReviewNotAllowed");
        }

        [Fact]
        public async Task SubmitReview_AsDoctor_Returns403()
        {
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PostAsJsonAsync(BaseUrl, new SubmitReviewRequest(_completedAppointmentId, 5, null));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task SubmitReview_RatingOutOfRange_Returns400()
        {
            AuthenticateAs(_patientUserId, "Patient");

            var response = await _client.PostAsJsonAsync(BaseUrl, new SubmitReviewRequest(_completedAppointmentId, 6, null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        private void AuthenticateAs(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }
    }
}
