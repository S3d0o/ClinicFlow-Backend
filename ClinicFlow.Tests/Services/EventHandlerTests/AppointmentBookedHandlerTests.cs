using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentBookedHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentBookedHandler _sut;

        public AppointmentBookedHandlerTests()
            => _sut = new AppointmentBookedHandler(_uow, _emailService, _clock, Substitute.For<ILogger<AppointmentBookedHandler>>());

        [Fact]
        public async Task HandleAsync_NotifiesPatientAndDoctor_AndEmailsThePatient()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            Assert.Equal([_patientUserId, _doctorUserId], _added.Select(n => n.UserId));
            Assert.Equal([NotificationType.AppointmentConfirmed, NotificationType.SystemAlert], _added.Select(n => n.Type));
            Assert.All(_added, n => Assert.Equal((AppointmentId, UtcNow), (n.RelatedEntityId!.Value, n.CreatedAt)));
            Assert.Equal($"Your appointment with Dr. Ahmed Kamal on {new DateOnly(2026, 10, 25)} at 10:00 has been booked.", _added[0].Message);
            await _emailService.Received(1).SendAppointmentConfirmationAsync(
                "mona@test.com", "Mona Hassan", "Ahmed Kamal", new DateOnly(2026, 10, 25), new TimeOnly(10, 0), _ct);
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
                _emailService.SendAppointmentConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly>(), _ct);
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
            await _emailService.ReceivedWithAnyArgs(1).SendAppointmentConfirmationAsync(default!, default!, default!, default, default, default);
        }

        [Fact]
        public async Task HandleAsync_WhenTheEmailFails_ThrowsSoTheOutboxRetries_AndKeepsTheNotifications()
        {
            StubAppointment();
            _emailService.SendAppointmentConfirmationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("SMTP down"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct));

            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.InApp, UtcNow);
            _uow.MessageDeliveries.DidNotReceive().Record(MessageId, DeliveryChannels.Email, Arg.Any<DateTime>());
        }

        [Fact]
        public async Task HandleAsync_WhenTheAppointmentIsGone_SendsNothing()
        {
            await _sut.HandleAsync(new AppointmentBooked(AppointmentId), MessageId, _ct);

            Assert.Empty(_added);
            await _emailService.DidNotReceiveWithAnyArgs().SendAppointmentConfirmationAsync(default!, default!, default!, default, default, default);
        }
    }
}
