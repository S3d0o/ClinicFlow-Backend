using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Events;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class DoctorApprovedHandlerTests : EventHandlerTestsBase
    {
        [Fact]
        public async Task HandleAsync_TellsTheDoctor()
        {
            await new DoctorApprovedHandler(_uow, _clock).HandleAsync(new DoctorApproved(_doctorUserId), MessageId, _ct);

            var notification = Assert.Single(_added);
            Assert.Equal((_doctorUserId, "Profile Approved", NotificationType.SystemAlert, (int?)null),
                (notification.UserId, notification.Title, notification.Type, notification.RelatedEntityId));
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.InApp, UtcNow);
        }
    }
}
