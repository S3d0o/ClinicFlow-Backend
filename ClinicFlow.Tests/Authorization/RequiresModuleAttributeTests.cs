using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.Authorization;

namespace ClinicFlow.Tests.Authorization
{
    // [RequiresModule] as a resource filter (docs/v2/adr/0008 decision 9): the action runs only while the module is on
    public class RequiresModuleAttributeTests
    {
        private readonly IEntitlements _entitlements = Substitute.For<IEntitlements>();
        private readonly ProblemDetailsFactory _problems = Substitute.For<ProblemDetailsFactory>();
        private readonly ActionContext _action;
        private bool _actionRan;

        public RequiresModuleAttributeTests()
        {
            _problems.CreateProblemDetails(Arg.Any<HttpContext>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
                .Returns(call => new ProblemDetails { Status = call.ArgAt<int?>(1), Title = call.ArgAt<string?>(2), Detail = call.ArgAt<string?>(4) });

            var services = new ServiceCollection()
                .AddSingleton(_entitlements)
                .AddSingleton(_problems)
                .AddLogging()
                .BuildServiceProvider();
            _action = new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor());
        }

        private async Task<ResourceExecutingContext> RunAsync(string module)
        {
            var context = new ResourceExecutingContext(_action, [], []);
            await new RequiresModuleAttribute(module).OnResourceExecutionAsync(context, () =>
            {
                _actionRan = true;
                return Task.FromResult(new ResourceExecutedContext(_action, []));
            });
            return context;
        }

        [Fact]
        public async Task ModuleOn_RunsTheAction()
        {
            _entitlements.IsEnabledAsync(Modules.Billing, Arg.Any<CancellationToken>()).Returns(true);

            var context = await RunAsync(Modules.Billing);

            Assert.True(_actionRan);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task ModuleOff_Returns403ModuleNotEnabled_WithoutRunningTheAction()
        {
            _entitlements.IsEnabledAsync(Modules.Billing, Arg.Any<CancellationToken>()).Returns(false);

            var context = await RunAsync(Modules.Billing);

            Assert.False(_actionRan);
            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value);
            Assert.Equal("Subscription.ModuleNotEnabled", problem.Title);
            Assert.Contains(Modules.Billing, problem.Detail);
        }

        [Fact]
        public void UnknownModule_Throws()
            => Assert.Throws<ArgumentException>(() => new RequiresModuleAttribute("Billing"));
    }
}
