using Shared.Authorization;

namespace ClinicFlow.Tests.Authorization
{
    // The role_scope claim (API_CONTRACT.md §2.1): written by the token factory, read back by HttpCurrentUser
    public class ClinicClaimsTests
    {
        [Fact]
        public void RoleScopeValue_WritesTheBranchesInOrder_OrAStarForTheOrganization()
        {
            Assert.Equal("Doctor=1,3", ClinicClaims.RoleScopeValue(new StaffAssignment("Doctor", new HashSet<int> { 3, 1 })));
            Assert.Equal("Manager=*", ClinicClaims.RoleScopeValue(new StaffAssignment("Manager", null)));
        }

        [Theory]
        [InlineData("Doctor=1,3")]
        [InlineData("Manager=*")]
        public void ParseRoleScope_ReadsWhatRoleScopeValueWrote(string value)
            => Assert.Equal(value, ClinicClaims.RoleScopeValue(ClinicClaims.ParseRoleScope(value)!));

        [Theory]
        [InlineData("Doctor")]
        [InlineData("=1")]
        [InlineData("Doctor=a")]
        public void ParseRoleScope_AMalformedValue_IsNull(string value)
            => Assert.Null(ClinicClaims.ParseRoleScope(value));
    }
}
