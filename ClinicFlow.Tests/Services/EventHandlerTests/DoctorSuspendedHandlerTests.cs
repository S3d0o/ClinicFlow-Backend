using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class DoctorSuspendedHandlerTests : EventHandlerTestsBase
    {
        [Fact]
        public async Task HandleAsync_TellsTheDoctor()
        {
            await new DoctorSuspendedHandler(_uow, _clock).HandleAsync(new DoctorSuspended(_doctorUserId), MessageId, _ct);

            var notification = Assert.Single(_added);
            Assert.Equal((_doctorUserId, "Profile Suspended"), (notification.UserId, notification.Title));
        }

        [Fact]
        public async Task HandleAsync_AlreadyDelivered_AddsNothing()
        {
            AlreadyDelivered(DeliveryChannels.InApp);

            await new DoctorSuspendedHandler(_uow, _clock).HandleAsync(new DoctorSuspended(_doctorUserId), MessageId, _ct);

            Assert.Empty(_added);
        }
    }
}
