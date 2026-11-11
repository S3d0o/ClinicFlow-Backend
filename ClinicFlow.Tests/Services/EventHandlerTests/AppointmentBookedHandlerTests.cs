using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Services.EventHandlers;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentBookedHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentBookedHandler _sut;

        public AppointmentBookedHandlerTests()
            => _sut = new AppointmentBookedHandler(_uow, _messenger, _clock, Substitute.For<ILogger<AppointmentBookedHandler>>());

        [Fact]
        public async Task HandleAsync_NotifiesPatientAndDoctor_AndEmailsThePatient()
        {
            var appointment = StubAppointment();

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            Assert.Equal([_patientUserId, _doctorUserId], _added.Select(n => n.UserId));
            Assert.Equal([NotificationType.AppointmentConfirmed, NotificationType.SystemAlert], _added.Select(n => n.Type));
            Assert.All(_added, n => Assert.Equal((AppointmentId, UtcNow), (n.RelatedEntityId!.Value, n.CreatedAt)));
            Assert.Equal($"Your appointment with Dr. Ahmed Kamal on {new DateOnly(2026, 10, 25)} at 10:00 has been booked.", _added[0].Message);

            var email = Assert.Single(_emails);
            Assert.Same(appointment.Patient.User, email.To);
            Assert.Equal(TemplateKeys.AppointmentConfirmed, email.TemplateKey);
            Assert.Equal(new Dictionary<string, string>
            {
                [TemplatePlaceholders.PatientName] = "Mona Hassan",
                [TemplatePlaceholders.DoctorName] = "Ahmed Kamal", // the template writes "Dr."
                [TemplatePlaceholders.Date] = "Sunday, October 25, 2026",
                [TemplatePlaceholders.Time] = "10:00 AM",
                [TemplatePlaceholders.BranchName] = "Maadi",
            }, email.Values);
        }

        [Fact]
        public async Task HandleAsync_APatientWhoReadsArabic_GetsTheDateTimeAndBranchInArabic()
        {
            StubAppointment().Patient.User.PreferredLanguage = Language.Ar;

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            var values = Assert.Single(_emails).Values;
            Assert.Equal(("الأحد، 25 أكتوبر 2026", "10:00 ص", "المعادي"),
                (values[TemplatePlaceholders.Date], values[TemplatePlaceholders.Time], values[TemplatePlaceholders.BranchName]));
        }

        [Fact]
        public async Task HandleAsync_GivesTheMessengerEveryPlaceholderTheTemplateOffers()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            // clinicName is the messenger's own
            var offered = MessageTemplateCatalog.Find(TemplateKeys.AppointmentConfirmed)!.Placeholders.Except([TemplatePlaceholders.ClinicName]);
            Assert.Equal(offered.Order(), Assert.Single(_emails).Values.Keys.Order());
        }

        [Fact]
        public async Task HandleAsync_RecordsEachChannelWithWhatItWrote()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            // Notifications and their delivery record are saved together; the email is recorded once it went out
            Received.InOrder(() =>
            {
                _uow.Notifications.AddRangeAsync(Arg.Any<IEnumerable<Notification>>(), _ct);
                _uow.MessageDeliveries.Record(MessageId, DeliveryChannels.InApp, UtcNow);
                _uow.SaveChangesAsync(_ct);
                _messenger.SendEmailAsync(Arg.Any<ApplicationUser>(), TemplateKeys.AppointmentConfirmed, Arg.Any<IReadOnlyDictionary<string, string>>(), _ct);
                _uow.MessageDeliveries.Record(MessageId, DeliveryChannels.Email, UtcNow);
                _uow.SaveChangesAsync(_ct);
            });
        }

        [Fact]
        public async Task HandleAsync_RetriedAfterTheNotifications_OnlySendsTheEmail()
        {
            // At least once: a retry skips the channels that already delivered
            StubAppointment();
            AlreadyDelivered(DeliveryChannels.InApp);

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            Assert.Empty(_added);
            Assert.Single(_emails);
        }

        [Fact]
        public async Task HandleAsync_WhenTheEmailFails_ThrowsSoTheOutboxRetries_AndKeepsTheNotifications()
        {
            StubAppointment();
            _messenger.SendEmailAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("SMTP down"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct));

            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.InApp, UtcNow);
            _uow.MessageDeliveries.DidNotReceive().Record(MessageId, DeliveryChannels.Email, Arg.Any<DateTime>());
        }

        [Theory]
        [InlineData(false, NotificationChannel.Both)]  // the address was never confirmed
        [InlineData(true, NotificationChannel.Phone)]  // the patient takes phone messages only
        public async Task HandleAsync_APatientWhoDoesNotTakeEmail_IsOnlyNotified(bool emailConfirmed, NotificationChannel channel)
        {
            var patient = StubAppointment().Patient.User;
            (patient.EmailConfirmed, patient.NotificationChannel) = (emailConfirmed, channel);

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            Assert.Equal(2, _added.Count);
            await NoEmailWasSent();
            _uow.MessageDeliveries.DidNotReceive().Record(MessageId, DeliveryChannels.Email, Arg.Any<DateTime>());
        }

        [Fact]
        public async Task HandleAsync_WhenTheAppointmentIsGone_SendsNothing()
        {
            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            Assert.Empty(_added);
            await NoEmailWasSent();
        }
    }
}
