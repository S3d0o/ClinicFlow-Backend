using Domain.Entities.AppModule;
using NSubstitute;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.SpecialtyServiceTests
{
    public class DeleteByIdAsyncTests : SpecialtyServiceTestsBase
    {
        private const int _id = 3;
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly Specialty _specialty = new() { Id = _id, Name = "Neurology" };

        public DeleteByIdAsyncTests()
        {
            _uow.Specialties.GetByIdAsync(_id, _ct).Returns(_specialty);
        }

        [Fact]
        public async Task DeleteByIdAsync_WhenNoDoctorsAssigned_DeletesAndSaves()
        {
            _uow.Specialties.HasAssignedDoctorsAsync(_id, _ct).Returns(false);

            var result = await _sut.DeleteByIdAsync(_id, _ct);

            Assert.True(result.IsSuccess);
            _uow.Specialties.Received(1).Delete(_specialty);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task DeleteByIdAsync_WhenDoctorsAssigned_ReturnsHasAssignedDoctorsAndDeletesNothing()
        {
            _uow.Specialties.HasAssignedDoctorsAsync(_id, _ct).Returns(true);

            var result = await _sut.DeleteByIdAsync(_id, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(SpecialtyErrors.HasAssignedDoctors, result.Errors);
            _uow.Specialties.DidNotReceive().Delete(Arg.Any<Specialty>());
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeleteByIdAsync_WhenNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.DeleteByIdAsync(999, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(SpecialtyErrors.NotFound(999), result.Errors);
            await _uow.Specialties.DidNotReceive().HasAssignedDoctorsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
            _uow.Specialties.DidNotReceive().Delete(Arg.Any<Specialty>());
        }
    }
}
