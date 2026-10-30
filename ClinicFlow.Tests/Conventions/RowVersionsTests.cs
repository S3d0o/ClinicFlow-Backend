using Domain.Interfaces;
using Domain.ResultPattern;
using NSubstitute;
using Shared.Concurrency;
using Shared.Errors;

namespace ClinicFlow.Tests.Conventions
{
    // rowVersion on the wire and the 409 Concurrency.Conflict helper (docs/v2/07_API_DESIGN.md §3, P1 PR 0)
    public class RowVersionsTests
    {
        private static readonly byte[] Current = [0, 0, 0, 0, 0, 0, 0x1F, 0xA3];

        [Fact]
        public void Encode_ThenTryDecode_RoundTrips()
        {
            var encoded = RowVersions.Encode(Current);

            Assert.True(RowVersions.TryDecode(encoded, out var decoded));
            Assert.Equal(Current, decoded);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not base64!")]
        [InlineData("AAAAAAAAAA==")]     // 7 bytes
        [InlineData("AAAAAAAAAAAA")]     // 9 bytes
        [InlineData("AAAAAAAAAAAAAAAA")] // 12 bytes
        public void TryDecode_NotAnEightByteBase64Value_Fails(string? value)
        {
            Assert.False(RowVersions.TryDecode(value, out var decoded));
            Assert.Empty(decoded);
        }

        [Fact]
        public void Check_SameRowVersion_IsOk()
            => Assert.True(RowVersions.Check(Current, RowVersions.Encode(Current)).IsSuccess);

        [Fact]
        public void Check_StaleRowVersion_IsConcurrencyConflict()
        {
            var result = RowVersions.Check(Current, RowVersions.Encode([0, 0, 0, 0, 0, 0, 0x1F, 0xA2]));

            Assert.True(result.IsFailure);
            Assert.Equal(ConcurrencyErrors.Conflict, result.Errors.Single());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("garbage")]
        public void Check_UndecodableRowVersion_IsConcurrencyConflict(string? sent)
            => Assert.Equal(ConcurrencyErrors.Conflict, RowVersions.Check(Current, sent).Errors.Single());

        [Fact]
        public void ConcurrencyConflict_Is409WithTheContractCode()
        {
            Assert.Equal("Concurrency.Conflict", ConcurrencyErrors.Conflict.Code);
            Assert.Equal(ErrorType.Conflict, ConcurrencyErrors.Conflict.Type);
        }

        [Fact]
        public async Task SaveOrConflictAsync_Saved_IsOk()
        {
            var uow = Substitute.For<IUnitOfWork>();
            uow.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(true);

            var result = await uow.SaveOrConflictAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task SaveOrConflictAsync_RowVersionChanged_IsConcurrencyConflict()
        {
            var uow = Substitute.For<IUnitOfWork>();
            uow.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(false);

            var result = await uow.SaveOrConflictAsync(CancellationToken.None);

            Assert.Equal(ConcurrencyErrors.Conflict, result.Errors.Single());
        }

        [Fact]
        public async Task SaveOrConflictAsync_PassesTheToken()
        {
            var uow = Substitute.For<IUnitOfWork>();
            using var cts = new CancellationTokenSource();

            await uow.SaveOrConflictAsync(cts.Token);

            await uow.Received(1).TrySaveChangesAsync(cts.Token);
        }
    }
}
