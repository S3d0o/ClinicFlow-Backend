using Presentation.Serialization;
using System.Text.Json;

namespace ClinicFlow.Tests.Conventions
{
    // The /api/v2 JSON rules (API_CONTRACT.md §13.1): ids are strings both ways, times of day are "HH:mm", and the
    // inherited routes' settings (ApiJson.Configure) keep numbers and "HH:mm:ss"
    public class V2JsonTests
    {
        public enum Colour { Red, Green }

        public record Sample(
            int Id,
            int BranchId,
            int? DoctorId,
            int? RoomId,
            List<int> ServiceIds,
            int[] RoomIds,
            IReadOnlyList<int> StaffIds,
            int Count,
            int IsPaid,
            Guid UserId,
            TimeOnly StartTime,
            TimeOnly? EndTime,
            TimeOnly? BreakTime,
            Colour Colour);

        public record Wrapper(int Id, List<Sample> Items);

        private static readonly Sample Value = new(
            Id: 42, BranchId: 7, DoctorId: 9, RoomId: null,
            ServiceIds: [1, 2], RoomIds: [3], StaffIds: [4, 5],
            Count: 3, IsPaid: 1, UserId: Guid.Parse("8a1e5bde-3c4e-4c51-9d0e-6f7f9b1f2a10"),
            StartTime: new TimeOnly(9, 30), EndTime: new TimeOnly(10, 0, 45), BreakTime: null,
            Colour: Colour.Green);

        private static JsonSerializerOptions V2()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            ApiJson.ConfigureV2(options);
            return options;
        }

        private static JsonSerializerOptions Inherited()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            ApiJson.Configure(options);
            return options;
        }

        private static JsonElement WriteV2(object value) => JsonSerializer.SerializeToElement(value, V2());

        // ── Ids ──────────────────────────────────────────────────────────────

        [Fact]
        public void Write_IntIds_AreStrings()
        {
            var json = WriteV2(Value);

            Assert.Equal("42", json.GetProperty("id").GetString());
            Assert.Equal("7", json.GetProperty("branchId").GetString());
            Assert.Equal("9", json.GetProperty("doctorId").GetString());
        }

        [Fact]
        public void Write_NullId_StaysNull()
            => Assert.Equal(JsonValueKind.Null, WriteV2(Value).GetProperty("roomId").ValueKind);

        [Fact]
        public void Write_IdCollections_AreArraysOfStrings()
        {
            var json = WriteV2(Value);

            Assert.Equal(["1", "2"], json.GetProperty("serviceIds").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(["3"], json.GetProperty("roomIds").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(["4", "5"], json.GetProperty("staffIds").EnumerateArray().Select(e => e.GetString()));
        }

        [Fact]
        public void Write_IntsThatAreNotIds_StayNumbers()
        {
            var json = WriteV2(Value);

            Assert.Equal(3, json.GetProperty("count").GetInt32());
            Assert.Equal(1, json.GetProperty("isPaid").GetInt32());
        }

        [Fact]
        public void Write_GuidId_IsTheUsualGuidString()
            => Assert.Equal("8a1e5bde-3c4e-4c51-9d0e-6f7f9b1f2a10", WriteV2(Value).GetProperty("userId").GetString());

        [Fact]
        public void Write_NestedObjects_GetTheSameRules()
        {
            var json = WriteV2(new Wrapper(1, [Value]));

            Assert.Equal("1", json.GetProperty("id").GetString());
            Assert.Equal("42", json.GetProperty("items")[0].GetProperty("id").GetString());
        }

        [Fact]
        public void Write_EnumsAreStillStrings()
            => Assert.Equal("Green", WriteV2(Value).GetProperty("colour").GetString());

        [Fact]
        public void RoundTrip_Record_ReadsIdsFromStrings()
        {
            var options = V2();

            var read = JsonSerializer.Deserialize<Sample>(JsonSerializer.Serialize(Value, options), options)!;

            // Collections compare by reference in a record, and "HH:mm" drops EndTime's seconds
            Assert.Equal(Value with
            {
                ServiceIds = read.ServiceIds, RoomIds = read.RoomIds, StaffIds = read.StaffIds, EndTime = new TimeOnly(10, 0)
            }, read);
            Assert.Equal(Value.ServiceIds, read.ServiceIds);
            Assert.Equal(Value.RoomIds, read.RoomIds);
            Assert.Equal(Value.StaffIds, read.StaffIds);
        }

        [Fact]
        public void Read_IdsAsNumbers_AreAcceptedToo()
        {
            var read = JsonSerializer.Deserialize<Sample>(
                """{"id": 42, "branchId": "7", "doctorId": 9, "serviceIds": [1, "2"], "roomIds": [], "staffIds": [], "startTime": "09:30"}""",
                V2())!;

            Assert.Equal(42, read.Id);
            Assert.Equal(7, read.BranchId);
            Assert.Equal(9, read.DoctorId);
            Assert.Equal([1, 2], read.ServiceIds);
        }

        [Theory]
        [InlineData("""{"id": "abc"}""")]
        [InlineData("""{"id": "-1"}""")]
        [InlineData("""{"id": "4.2"}""")]
        [InlineData("""{"id": true}""")]
        [InlineData("""{"serviceIds": "1"}""")]
        [InlineData("""{"serviceIds": ["x"]}""")]
        public void Read_InvalidId_Throws(string json)
            => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Sample>(json, V2()));

        [Fact]
        public void Read_NullableIdAsNull_IsNull()
            => Assert.Null(JsonSerializer.Deserialize<Sample>("""{"doctorId": null, "startTime": "09:00"}""", V2())!.DoctorId);

        [Fact]
        public void Inherited_IdsStayNumbers()
        {
            var json = JsonSerializer.SerializeToElement(Value, Inherited());

            Assert.Equal(JsonValueKind.Number, json.GetProperty("id").ValueKind);
            Assert.Equal(JsonValueKind.Number, json.GetProperty("serviceIds")[0].ValueKind);
        }

        // ── Times ────────────────────────────────────────────────────────────

        [Fact]
        public void Write_Times_AreHoursAndMinutes()
        {
            var json = WriteV2(Value);

            Assert.Equal("09:30", json.GetProperty("startTime").GetString());
            Assert.Equal("10:00", json.GetProperty("endTime").GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("breakTime").ValueKind);
        }

        [Theory]
        [InlineData("09:30", 9, 30, 0)]
        [InlineData("09:30:15", 9, 30, 15)]
        [InlineData("23:59", 23, 59, 0)]
        public void Read_Times_AcceptHoursAndMinutesWithOrWithoutSeconds(string time, int hour, int minute, int second)
        {
            var read = JsonSerializer.Deserialize<Sample>($$"""{"startTime": "{{time}}", "endTime": "{{time}}"}""", V2())!;

            Assert.Equal(new TimeOnly(hour, minute, second), read.StartTime);
            Assert.Equal(new TimeOnly(hour, minute, second), read.EndTime);
        }

        [Theory]
        [InlineData("""{"startTime": "9:30 AM"}""")]
        [InlineData("""{"startTime": "24:00"}""")]
        [InlineData("""{"startTime": 930}""")]
        public void Read_InvalidTime_Throws(string json)
            => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Sample>(json, V2()));

        [Fact]
        public void Inherited_TimesKeepSeconds()
            => Assert.Equal("09:30:00", JsonSerializer.SerializeToElement(Value, Inherited()).GetProperty("startTime").GetString());
    }
}
