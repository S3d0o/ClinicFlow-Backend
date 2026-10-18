using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Doctor;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public class GetSlotsByDateAsyncTests : DoctorServiceTestsBase
    {
        private const int _doctorId = 5;
        private readonly DateOnly _date = new(2026, 10, 18);
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task GetSlotsByDateAsync_WhenSlotsAvailable_MapsThemInQueryOrder()
        {
            _uow.Slots.GetAvailableByDoctorAndDateAsync(_doctorId, _date, _ct).Returns(new List<AppointmentSlot>
            {
                new() { Id = 1, Date = _date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30), Status = SlotStatus.Available },
                new() { Id = 2, Date = _date, StartTime = new TimeOnly(9, 30), EndTime = new TimeOnly(10, 0), Status = SlotStatus.Available }
            });

            var result = await _sut.GetSlotsByDateAsync(_doctorId, _date, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[]
            {
                new SlotResponse { Id = 1, Date = _date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30), Status = SlotStatus.Available },
                new SlotResponse { Id = 2, Date = _date, StartTime = new TimeOnly(9, 30), EndTime = new TimeOnly(10, 0), Status = SlotStatus.Available }
            }, result.Value);
        }

        [Fact]
        public async Task GetSlotsByDateAsync_WhenNoSlots_ReturnsEmptyListNotAnError()
        {
            // Documented in the contract (#11): an unknown doctor or a day off returns []
            _uow.Slots.GetAvailableByDoctorAndDateAsync(_doctorId, _date, _ct).Returns(new List<AppointmentSlot>());

            var result = await _sut.GetSlotsByDateAsync(_doctorId, _date, _ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }
}
