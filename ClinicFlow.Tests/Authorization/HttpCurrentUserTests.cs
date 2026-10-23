using Microsoft.AspNetCore.Http;
using Presentation.Authorization;
using Shared.Authorization;
using System.Net;
using System.Security.Claims;

namespace ClinicFlow.Tests.Authorization
{
    public class HttpCurrentUserTests
    {
        private static HttpCurrentUser For(HttpContext? context) => new(new HttpContextAccessor { HttpContext = context });

        [Fact]
        public void SignedInUser_ExposesIdRolesAndIp()
        {
            var id = Guid.NewGuid();
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, Roles.Doctor), new Claim(ClaimTypes.Role, Roles.Manager)],
                    "Bearer")),
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

            var sut = For(context);

            Assert.True(sut.IsAuthenticated);
            Assert.Equal(id, sut.UserId);
            Assert.Equal([Roles.Doctor, Roles.Manager], sut.Roles);
            Assert.True(sut.IsInRole(Roles.Manager));
            Assert.False(sut.IsInRole(Roles.Admin));
            Assert.Equal("203.0.113.7", sut.IpAddress);
        }

        [Fact]
        public void AnonymousRequest_HasNoRolesAndNoUserId()
        {
            // A role claim on an unauthenticated identity must not count
            var sut = For(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Admin)])) });

            Assert.False(sut.IsAuthenticated);
            Assert.Empty(sut.Roles);
            Assert.False(sut.IsInRole(Roles.Admin));
            Assert.Throws<InvalidOperationException>(() => sut.UserId);
        }

        [Fact]
        public void OutsideARequest_IsAnonymousWithNoIp()
        {
            // e.g. a background job resolving a service that uses ICurrentUser
            var sut = For(null);

            Assert.False(sut.IsAuthenticated);
            Assert.Null(sut.IpAddress);
        }
    }
}
