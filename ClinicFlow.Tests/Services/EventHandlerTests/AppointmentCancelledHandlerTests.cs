using ClinicFlow.Domain.Enums;
using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class AppointmentCancelledHandlerTests : EventHandlerTestsBase
    {
        private readonly AppointmentCancelledHandler _sut;

        public AppointmentCancelledHandlerTests()
            => _sut = new AppointmentCancelledHandler(_uow, _clock, Substitute.For<ILogger<AppointmentCancelledHandler>>());

        [Fact]
        public async Task HandleAsync_NotifiesPatientAndDoctor()
        {
            StubAppointment();

            await _sut.HandleAsync(new AppointmentCancelled(AppointmentId, CancelledBy.Patient), MessageId, _ct);

            Assert.Equal([_patientUserId, _doctorUserId], _added.Select(n => n.UserId));
            Assert.All(_added, n => Assert.Equal((NotificationType.AppointmentCancelled, AppointmentId), (n.Type, n.RelatedEntityId!.Value)));
            Assert.Equal($"Your appointment with Mona Hassan on {new DateOnly(2026, 10, 25)} at 10:00 has been cancelled.", _added[1].Message);
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.InApp, UtcNow);
        }

        [Fact]
        public async Task HandleAsync_AlreadyDelivered_AddsNothing()
        {
            StubAppointment();
            AlreadyDelivered(DeliveryChannels.InApp);

            await _sut.HandleAsync(new AppointmentCancelled(AppointmentId, CancelledBy.Patient), MessageId, _ct);

            Assert.Empty(_added);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
