using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Specialty;

namespace ClinicFlow.Tests.Services.SpecialtyServiceTests
{
    public class GetAllAsyncTests : SpecialtyServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetAllAsync_Always_PassesTheInactiveFilterAndMapsEveryField(bool includeInactive)
        {
            _uow.Specialties.GetAllAsync(_ct, includeInactive).Returns(new List<Specialty>
            {
                new() { Id = 1, Name = "Cardiology", Description = "Heart", IconUrl = "https://cdn.test/heart.svg", IsActive = true }
            });

            var result = await _sut.GetAllAsync(includeInactive, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new SpecialtyResponse(1, "Cardiology", "Heart", "https://cdn.test/heart.svg", true), Assert.Single(result.Value));
            await _uow.Specialties.Received(1).GetAllAsync(_ct, includeInactive);
        }
    }
}
