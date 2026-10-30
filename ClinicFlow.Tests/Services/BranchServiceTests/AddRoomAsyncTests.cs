using Domain.Entities.Tenancy;
using Domain.Enums;
using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public class AddRoomAsyncTests : BranchServiceTestsBase
    {
        [Fact]
        public async Task AddRoomAsync_NewName_AddsAnActiveRoomInATransaction()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());

            var result = await _sut.AddRoomAsync(BranchId, ARoomRequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(BranchId, result.Value.BranchId);
            Assert.Equal(RoomKind.Consultation, result.Value.Kind);
            Assert.True(result.Value.IsActive);
            _uow.Rooms.Received(1).Add(Arg.Is<Room>(r => r!.BranchId == BranchId && r.NameEn == "Room 1" && r.IsActive));
            await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddRoomAsync_UnknownBranch_ReturnsBranchNotFound()
        {
            var result = await _sut.AddRoomAsync(BranchId, ARoomRequest(), CancellationToken.None);

            Assert.Equal("Branch.NotFound", Assert.Single(result.Errors).Code);
            _uow.Rooms.DidNotReceive().Add(Arg.Any<Room>());
        }

        [Fact]
        public async Task AddRoomAsync_NameTakenAtTheBranch_ReturnsRoomNameTaken()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());
            _uow.Rooms.NameExistsAsync(BranchId, "Room 1", "غرفة ١", null, Arg.Any<CancellationToken>()).Returns(true);

            var result = await _sut.AddRoomAsync(BranchId, ARoomRequest(), CancellationToken.None);

            Assert.Equal(BranchErrors.RoomNameTaken, Assert.Single(result.Errors));
            _uow.Rooms.DidNotReceive().Add(Arg.Any<Room>());
        }

        [Fact]
        public async Task AddRoomAsync_BranchOutsideTheUsersScope_ReturnsOutOfScope()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());
            _accessGuard.CanAccessAsync(Permissions.Organization.RoomsManage,
                Arg.Is<AccessTarget>(t => t!.BranchId == BranchId), Arg.Any<CancellationToken>()).Returns(false);

            var result = await _sut.AddRoomAsync(BranchId, ARoomRequest(), CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            _uow.Rooms.DidNotReceive().Add(Arg.Any<Room>());
        }
    }
}
