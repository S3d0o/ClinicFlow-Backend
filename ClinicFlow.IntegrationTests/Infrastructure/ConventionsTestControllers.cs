using Domain.ResultPattern;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Presentation;
using Presentation.Controllers;
using Presentation.Controllers.V2;
using Shared.DTOs.Common;

namespace ClinicFlow.IntegrationTests.Infrastructure
{
    // Test-only endpoints for the /api/v2 conventions (API_CONTRACT.md §13.1, P1 PR 0). No /api/v2 endpoint exists yet,
    // so V2ConventionsTests adds these to its own host (ConventionsTestHost); the shared factory never sees them, which
    // keeps AuthorizationTests' allowlist about the real app

    public sealed record ConventionsSample(
        int Id, int BranchId, int? DoctorId, List<int> ServiceIds, int Count, TimeOnly StartTime, TimeOnly? EndTime);

    public sealed record ConventionsLine(decimal UnitPrice);

    public sealed record ConventionsInvoiceRequest(string? PatientName, List<ConventionsLine> Lines);

    public sealed class ConventionsInvoiceRequestValidator : AbstractValidator<ConventionsInvoiceRequest>
    {
        public ConventionsInvoiceRequestValidator()
        {
            RuleFor(x => x.PatientName).NotEmpty().WithMessage("Name is required.");
            RuleForEach(x => x.Lines).ChildRules(line =>
                line.RuleFor(l => l.UnitPrice).GreaterThan(0).WithMessage("Price must be positive."));
        }
    }

    public static class ConventionsSamples
    {
        public static readonly ConventionsSample Value =
            new(42, 7, null, [1, 2], 3, new TimeOnly(9, 30), new TimeOnly(17, 0, 30));

        public static readonly DateTime RetryAt = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        public static Error ErrorFor(string kind) => kind switch
        {
            "validation" => Error.Validation("Consent.Required", "Accept the terms"),
            "invalid-credentials" => Error.InvalidCredentials(),
            "unauthorized" => Error.Unauthorized("Auth.InvalidRefreshToken", "Refresh token is invalid or expired"),
            "forbidden" => Error.Forbidden("Auth.AccountNotActive", "Deactivated"),
            "not-found" => Error.NotFound("Branch.NotFound", "Unknown branch"),
            "conflict" => Error.Conflict("Concurrency.Conflict", "Changed"),
            "too-many" => Error.TooManyRequests("Otp.ResendTooSoon", "Wait").WithRetryAt(RetryAt),
            "attempts" => Error.Validation("Otp.InvalidCode", "Wrong code").WithAttemptsLeft(2),
            _ => Error.Failure()
        };
    }

    [Authorize]
    [Route(RoutePrefix + "/test-conventions")]
    public sealed class V2ConventionsTestController : V2ApiController
    {
        [HttpGet("ids")]
        public ActionResult<ConventionsSample> GetSample() => HandleResult(Result<ConventionsSample>.Ok(ConventionsSamples.Value));

        [HttpPost("echo")]
        public ActionResult<ConventionsSample> Echo(ConventionsSample request) => HandleResult(Result<ConventionsSample>.Ok(request));

        [HttpGet("errors/{kind}")]
        public IActionResult Fail(string kind) => HandleResult(Result.Fail(ConventionsSamples.ErrorFor(kind)));

        [HttpPost("invoices")]
        public IActionResult Validate(ConventionsInvoiceRequest request) => HandleResult(Result.Ok());

        [HttpGet("paged")]
        public ActionResult<IReadOnlyList<int>> Paged([FromQuery] PageRequest page)
        {
            var all = Enumerable.Range(1, 45).ToList();
            return HandlePagedResult(Result<PagedList<int>>.Ok(new PagedList<int>(all.Skip(page.Skip()).Take(page.PageSize).ToList(), all.Count)));
        }

        [HttpGet("limited")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public IActionResult Limited() => HandleResult(Result.Ok());
    }

    // The same sample and errors on an inherited-style route, to show the V2 rules stay on /api/v2
    [Authorize]
    [Route("api/test-conventions")]
    public sealed class InheritedConventionsTestController : ApiController
    {
        [HttpGet("ids")]
        public ActionResult<ConventionsSample> GetSample() => HandleResult(Result<ConventionsSample>.Ok(ConventionsSamples.Value));

        [HttpGet("errors/{kind}")]
        public IActionResult Fail(string kind) => HandleResult(Result.Fail(ConventionsSamples.ErrorFor(kind)));

        [HttpPost("invoices")]
        public IActionResult Validate(ConventionsInvoiceRequest request) => HandleResult(Result.Ok());

        [HttpGet("limited")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public IActionResult Limited() => HandleResult(Result.Ok());
    }
}
