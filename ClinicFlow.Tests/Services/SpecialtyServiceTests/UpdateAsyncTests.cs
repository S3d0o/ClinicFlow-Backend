using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Specialty;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.SpecialtyServiceTests
{
    public class UpdateAsyncTests : SpecialtyServiceTestsBase
    {
        private const int _id = 3;
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly Specialty _specialty = new() { Id = _id, Name = "Neurology", Description = "Old", IsActive = true };

        public UpdateAsyncTests()
        {
            // Admin updates and deletes reach inactive specialties too (REVIEW.md L6)
            _uow.Specialties.GetByIdAsync(_id, _ct, includeInactive: true).Returns(_specialty);
        }

        [Fact]
        public async Task UpdateAsync_WhenRenamedToFreeName_UpdatesAndReturnsIt()
        {
            _uow.Specialties.GetByNameAsync("Neurosurgery", _ct).Returns((Specialty?)null);

            var result = await _sut.UpdateAsync(_id, new SpecialtyRequest("Neurosurgery", "Surgery of the nervous system", null), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("Neurosurgery", _specialty.Name);
            Assert.Equal("Surgery of the nervous system", _specialty.Description);
            Assert.True(_specialty.IsActive);
            await _uow.Received(1).SaveChangesAsync(_ct);
            Assert.Equal(new SpecialtyResponse(_id, "Neurosurgery", "Surgery of the nervous system", null, true), result.Value);
        }

        [Fact]
        public async Task UpdateAsync_WhenKeepingItsOwnName_Succeeds()
        {
            // The name lookup finds this same specialty, which is not a conflict
            _uow.Specialties.GetByNameAsync("Neurology", _ct).Returns(_specialty);

            var result = await _sut.UpdateAsync(_id, new SpecialtyRequest("Neurology", "New description", null), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("New description", _specialty.Description);
        }

        [Fact]
        public async Task UpdateAsync_WhenRenamedToAnotherSpecialtysName_ReturnsNameAlreadyExistsAndChangesNothing()
        {
            _uow.Specialties.GetByNameAsync("Cardiology", _ct).Returns(new Specialty { Id = 1, Name = "Cardiology" });

            var result = await _sut.UpdateAsync(_id, new SpecialtyRequest("Cardiology", null, null), _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(SpecialtyErrors.NameAlreadyExists, result.Errors);
            Assert.Equal("Neurology", _specialty.Name);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_WithIsActiveTrue_ReactivatesAnInactiveSpecialty()
        {
            // REVIEW.md L6: an inactive specialty couldn't be read, updated or reactivated
            _specialty.IsActive = false;

            var result = await _sut.UpdateAsync(_id, new SpecialtyRequest("Neurology", "Old", null, IsActive: true), _ct);

            Assert.True(result.IsSuccess);
            Assert.True(_specialty.IsActive);
            Assert.True(result.Value.IsActive);
        }

        [Fact]
        public async Task UpdateAsync_WithIsActiveFalse_Deactivates()
        {
            var result = await _sut.UpdateAsync(_id, new SpecialtyRequest("Neurology", "Old", null, IsActive: false), _ct);

            Assert.True(result.IsSuccess);
            Assert.False(_specialty.IsActive);
        }

        [Fact]
        public async Task UpdateAsync_WithoutIsActive_KeepsTheCurrentState()
        {
            _specialty.IsActive = false;

            var result = await _sut.UpdateAsync(_id, new SpecialtyRequest("Neurology", "New description", null), _ct);

            Assert.True(result.IsSuccess);
            Assert.False(_specialty.IsActive);
        }

        [Fact]
        public async Task UpdateAsync_WhenNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.UpdateAsync(999, new SpecialtyRequest("Anything", null, null), _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(SpecialtyErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
