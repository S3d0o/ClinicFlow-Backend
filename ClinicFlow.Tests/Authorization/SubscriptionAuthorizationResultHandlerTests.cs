using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.Authorization;

namespace ClinicFlow.Tests.Authorization
{
    // A permission refused only for its module answers 403 Subscription.ModuleNotEnabled (docs/v2/adr/0008 decision
    // 10); every other result is the framework's default
    public class SubscriptionAuthorizationResultHandlerTests
    {
        private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        private readonly IProblemDetailsService _problems = Substitute.For<IProblemDetailsService>();
        private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
        private readonly DefaultHttpContext _http;
        private ProblemDetailsContext? _written;
        private bool _nextRan;

        public SubscriptionAuthorizationResultHandlerTests()
        {
            _problems.TryWriteAsync(Arg.Do<ProblemDetailsContext>(c => _written = c)).Returns(ValueTask.FromResult(true));
            _http = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(_problems).AddSingleton(_authentication).BuildServiceProvider(),
            };
        }

        private Task HandleAsync(PolicyAuthorizationResult result)
            => new SubscriptionAuthorizationResultHandler().HandleAsync(_ => { _nextRan = true; return Task.CompletedTask; }, _http, Policy, result);

        [Fact]
        public async Task ForbiddenForAModule_Writes403WithTheSubscriptionCode()
        {
            var handler = new PermissionAuthorizationHandler(Substitute.For<IEntitlements>());
            var failure = AuthorizationFailure.Failed([new ModuleNotEnabledReason(handler, Modules.FrontDesk)]);

            await HandleAsync(PolicyAuthorizationResult.Forbid(failure));

            Assert.False(_nextRan);
            Assert.Equal(StatusCodes.Status403Forbidden, _http.Response.StatusCode);
            Assert.NotNull(_written);
            Assert.Equal(StatusCodes.Status403Forbidden, _written.ProblemDetails.Status);
            Assert.Equal("Subscription.ModuleNotEnabled", _written.ProblemDetails.Title);
            Assert.Contains(Modules.FrontDesk, _written.ProblemDetails.Detail);
            await _authentication.DidNotReceiveWithAnyArgs().ForbidAsync(default!, default, default);
        }

        [Fact]
        public async Task ForbiddenForAnotherReason_IsTheDefaultForbid()
        {
            await HandleAsync(PolicyAuthorizationResult.Forbid(AuthorizationFailure.ExplicitFail()));

            Assert.Null(_written);
            await _authentication.Received(1).ForbidAsync(_http, Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
        }

        [Fact]
        public async Task Success_RunsTheRequest()
        {
            await HandleAsync(PolicyAuthorizationResult.Success());

            Assert.True(_nextRan);
            Assert.Null(_written);
        }
    }
}
