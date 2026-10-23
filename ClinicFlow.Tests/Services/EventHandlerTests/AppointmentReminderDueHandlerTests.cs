using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentReminderDueHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentReminderDueHandler _sut;

        public AppointmentReminderDueHandlerTests()
            => _sut = new AppointmentReminderDueHandler(_uow, _emailService, _clock, Substitute.For<ILogger<AppointmentReminderDueHandler>>());

        [Fact]
        public async Task HandleAsync_EmailsAndNotifiesThePatient()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            await _emailService.Received(1).SendAppointmentReminderAsync(
                "mona@test.com", "Mona Hassan", "Ahmed Kamal", new DateOnly(2026, 10, 25), new TimeOnly(10, 0), _ct);
            var notification = Assert.Single(_added);
            Assert.Equal((_patientUserId, NotificationType.AppointmentReminder), (notification.UserId, notification.Type));
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.Email, UtcNow);
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.InApp, UtcNow);
        }

        [Theory]
        [InlineData(AppointmentStatus.Cancelled)]
        [InlineData(AppointmentStatus.Completed)]
        public async Task HandleAsync_WhenTheVisitIsNoLongerConfirmed_SendsNothing(AppointmentStatus status)
        {
            // Cancelled between the reminder job and the dispatch
            StubAppointment().WithStatus(status);

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            await _emailService.DidNotReceiveWithAnyArgs().SendAppointmentReminderAsync(default!, default!, default!, default, default, default);
            Assert.Empty(_added);
        }

        [Fact]
        public async Task HandleAsync_RetriedAfterTheEmail_OnlyNotifies()
        {
            StubAppointment();
            AlreadyDelivered(DeliveryChannels.Email);

            await _sut.HandleAsync(new AppointmentReminderDue(AppointmentId), MessageId, _ct);

            await _emailService.DidNotReceiveWithAnyArgs().SendAppointmentReminderAsync(default!, default!, default!, default, default, default);
            Assert.Single(_added);
        }
    }
}
