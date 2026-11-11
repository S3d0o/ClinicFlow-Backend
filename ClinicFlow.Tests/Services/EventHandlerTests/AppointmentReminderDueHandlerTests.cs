using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentReminderDueHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentReminderDueHandler _sut;

        public AppointmentReminderDueHandlerTests()
            => _sut = new AppointmentReminderDueHandler(_uow, _messenger, _clock, Substitute.For<ILogger<AppointmentReminderDueHandler>>());

        [Fact]
        public async Task HandleAsync_EmailsAndNotifiesThePatient()
        {
            var appointment = StubAppointment();

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            var email = Assert.Single(_emails);
            Assert.Same(appointment.Patient.User, email.To);
            Assert.Equal(TemplateKeys.AppointmentReminder, email.TemplateKey);
            Assert.Equal(("Mona Hassan", "Ahmed Kamal", "Sunday, October 25, 2026", "10:00 AM"),
                (email.Values[TemplatePlaceholders.PatientName], email.Values[TemplatePlaceholders.DoctorName],
                 email.Values[TemplatePlaceholders.Date], email.Values[TemplatePlaceholders.Time]));
            var notification = Assert.Single(_added);
            Assert.Equal((_patientUserId, NotificationType.AppointmentReminder), (notification.UserId, notification.Type));
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.Email, UtcNow);
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.InApp, UtcNow);
        }

        [Fact]
        public async Task HandleAsync_APatientWhoReadsArabic_GetsTheDateAndTimeInArabic()
        {
            StubAppointment().Patient.User.PreferredLanguage = Language.Ar;

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            var values = Assert.Single(_emails).Values;
            Assert.Equal(("الأحد، 25 أكتوبر 2026", "10:00 ص"), (values[TemplatePlaceholders.Date], values[TemplatePlaceholders.Time]));
        }

        [Theory]
        [InlineData(AppointmentStatus.Cancelled)]
        [InlineData(AppointmentStatus.Completed)]
        public async Task HandleAsync_WhenTheVisitIsNoLongerConfirmed_SendsNothing(AppointmentStatus status)
        {
            // Cancelled between the reminder job and the dispatch
            StubAppointment().WithStatus(status);

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            await NoEmailWasSent();
            Assert.Empty(_added);
        }

        [Fact]
        public async Task HandleAsync_RetriedAfterTheEmail_OnlyNotifies()
        {
            StubAppointment();
            AlreadyDelivered(DeliveryChannels.Email);

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            await NoEmailWasSent();
            Assert.Single(_added);
        }

        [Fact]
        public async Task HandleAsync_APatientWithoutAConfirmedEmail_IsOnlyNotified()
        {
            StubAppointment().Patient.User.EmailConfirmed = false;

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            await NoEmailWasSent();
            Assert.Single(_added);
        }
    }
}
