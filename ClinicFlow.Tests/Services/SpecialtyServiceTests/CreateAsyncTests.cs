using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Specialty;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.SpecialtyServiceTests
{
    public class CreateAsyncTests : SpecialtyServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly SpecialtyRequest _request = new("Neurology", "Brain and nerves", "https://cdn.test/neuro.svg");

        [Fact]
        public async Task CreateAsync_WhenNameIsNew_AddsSpecialtyAndReturnsIt()
        {
            _uow.Specialties.GetByNameAsync("Neurology", _ct).Returns((Specialty?)null);
            Specialty? added = null;
            await _uow.Specialties.CreateAsync(Arg.Do<Specialty>(s => { added = s; s.Id = 8; }), Arg.Any<CancellationToken>());

            var result = await _sut.CreateAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(added);
            Assert.Equal("Neurology", added!.Name);
            Assert.Equal("Brain and nerves", added.Description);
            Assert.Equal("https://cdn.test/neuro.svg", added.IconUrl);
            await _uow.Received(1).SaveChangesAsync(_ct);
            Assert.Equal(8, result.Value.Id);
            Assert.Equal("Neurology", result.Value.Name);
        }

        [Fact]
        public async Task CreateAsync_WhenNameExists_ReturnsNameAlreadyExistsAndAddsNothing()
        {
            _uow.Specialties.GetByNameAsync("Neurology", _ct).Returns(new Specialty { Id = 3, Name = "Neurology" });

            var result = await _sut.CreateAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(SpecialtyErrors.NameAlreadyExists, result.Errors);
            await _uow.Specialties.DidNotReceive().CreateAsync(Arg.Any<Specialty>(), Arg.Any<CancellationToken>());
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
