using Domain.Enums;
using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public class UpdateRoomAsyncTests : BranchServiceTestsBase
    {
        [Fact]
        public async Task UpdateRoomAsync_ValidRequest_ReplacesNamesAndKindAndSaves()
        {
            var room = ARoom();
            _uow.Rooms.GetByIdAsync(RoomId, Arg.Any<CancellationToken>()).Returns(room);

            var result = await _sut.UpdateRoomAsync(RoomId, ARoomRequest(isActive: false), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("Room 1", room.NameEn);
            Assert.Equal(RoomKind.Consultation, room.Kind);
            Assert.False(room.IsActive);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateRoomAsync_NoIsActive_KeepsTheRoomsState()
        {
            var room = ARoom();
            _uow.Rooms.GetByIdAsync(RoomId, Arg.Any<CancellationToken>()).Returns(room);

            await _sut.UpdateRoomAsync(RoomId, ARoomRequest(isActive: null), CancellationToken.None);

            Assert.True(room.IsActive);
        }

        [Fact]
        public async Task UpdateRoomAsync_NameTakenByAnotherRoom_ReturnsRoomNameTaken()
        {
            _uow.Rooms.GetByIdAsync(RoomId, Arg.Any<CancellationToken>()).Returns(ARoom());
            _uow.Rooms.NameExistsAsync(BranchId, "Room 1", "غرفة ١", RoomId, Arg.Any<CancellationToken>()).Returns(true);

            var result = await _sut.UpdateRoomAsync(RoomId, ARoomRequest(), CancellationToken.None);

            Assert.Equal(BranchErrors.RoomNameTaken, Assert.Single(result.Errors));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateRoomAsync_UnknownRoom_ReturnsRoomNotFound()
        {
            var result = await _sut.UpdateRoomAsync(RoomId, ARoomRequest(), CancellationToken.None);

            Assert.Equal("Room.NotFound", Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task UpdateRoomAsync_RoomOutsideTheUsersScope_ReturnsRoomNotFound()
        {
            _uow.Rooms.GetByIdAsync(RoomId, Arg.Any<CancellationToken>()).Returns(ARoom());
            _accessGuard.CanAccessAsync(Permissions.Organization.RoomsManage,
                Arg.Is<AccessTarget>(t => t!.BranchId == BranchId), Arg.Any<CancellationToken>()).Returns(false);

            var result = await _sut.UpdateRoomAsync(RoomId, ARoomRequest(), CancellationToken.None);

            Assert.Equal("Room.NotFound", Assert.Single(result.Errors).Code);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
