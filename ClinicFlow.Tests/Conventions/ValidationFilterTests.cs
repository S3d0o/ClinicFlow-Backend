using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Presentation.Filters;
using Presentation.Validators;
using Shared.DTOs.Common;

namespace ClinicFlow.Tests.Conventions
{
    // The global ValidationFilter: camelCase keys on every route (P1 plan Q-F) and the shared PageRequest rule for every
    // filter derived from it (contract §13.1 "Lists")
    public class ValidationFilterTests
    {
        public sealed record LineRequest(decimal UnitPrice);

        public sealed record InvoiceRequest(string? PatientName, List<LineRequest> Lines);

        public sealed class InvoiceRequestValidator : AbstractValidator<InvoiceRequest>
        {
            public InvoiceRequestValidator()
            {
                RuleFor(x => x.PatientName).NotEmpty().WithMessage("Name is required.");
                RuleForEach(x => x.Lines).ChildRules(line =>
                    line.RuleFor(l => l.UnitPrice).GreaterThan(0).WithMessage("Price must be positive."));
            }
        }

        public sealed record BranchFilterRequest : PageRequest
        {
            public string? Search { get; init; }
        }

        public sealed class BranchFilterRequestValidator : AbstractValidator<BranchFilterRequest>
        {
            public BranchFilterRequestValidator()
                => RuleFor(x => x.Search).MaximumLength(5).WithMessage("Search is too long.");
        }

        private static async Task<(ActionExecutingContext Context, bool NextCalled)> RunAsync(object argument)
        {
            var services = new ServiceCollection()
                .AddSingleton<ProblemDetailsFactory, TestProblemDetailsFactory>()
                .AddScoped<IValidator<InvoiceRequest>, InvoiceRequestValidator>()
                .AddScoped<IValidator<BranchFilterRequest>, BranchFilterRequestValidator>()
                .AddScoped<IValidator<PageRequest>, PageRequestValidator>()
                .BuildServiceProvider();

            var actionContext = new ActionContext(
                new DefaultHttpContext { RequestServices = services }, new RouteData(), new ActionDescriptor());
            var context = new ActionExecutingContext(
                actionContext, [], new Dictionary<string, object?> { ["request"] = argument }, controller: new object());

            var nextCalled = false;
            await new ValidationFilter(NullLogger<ValidationFilter>.Instance).OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
            });

            return (context, nextCalled);
        }

        private static IDictionary<string, string[]> Errors(ActionExecutingContext context)
        {
            var result = Assert.IsType<BadRequestObjectResult>(context.Result);
            return Assert.IsType<ValidationProblemDetails>(result.Value).Errors;
        }

        [Fact]
        public async Task InvalidBody_KeysAreCamelCasePropertyPaths()
        {
            var (context, nextCalled) = await RunAsync(new InvoiceRequest(null, [new LineRequest(5), new LineRequest(0)]));

            Assert.False(nextCalled);
            var errors = Errors(context);
            Assert.Equal(["patientName", "lines[1].unitPrice"], errors.Keys.Order(StringComparer.Ordinal).Reverse());
            Assert.Equal(["Price must be positive."], errors["lines[1].unitPrice"]);
        }

        [Fact]
        public async Task ValidBody_CallsTheAction()
        {
            var (context, nextCalled) = await RunAsync(new InvoiceRequest("Mona", [new LineRequest(5)]));

            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }

        [Fact]
        public async Task FilterDerivedFromPageRequest_GetsThePagingRuleAndItsOwn()
        {
            var (context, _) = await RunAsync(new BranchFilterRequest { PageNumber = 0, PageSize = 101, Search = "too long" });

            var errors = Errors(context);
            Assert.Equal(["pageNumber", "pageSize", "search"], errors.Keys.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task FilterDerivedFromPageRequest_DefaultsPass()
        {
            var (_, nextCalled) = await RunAsync(new BranchFilterRequest());

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task PageRequestItself_IsValidated()
        {
            var (context, _) = await RunAsync(new PageRequest { PageSize = 0 });

            Assert.Equal(["pageSize"], Errors(context).Keys);
        }
    }
}
