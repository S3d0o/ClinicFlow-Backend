using Domain.ResultPattern;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Presentation.Controllers;
using Presentation.Controllers.V2;
using System.Reflection;

namespace ClinicFlow.Tests.Conventions
{
    // How a failed Result becomes a response (API_CONTRACT.md §3.1 for inherited routes, §13.1 for /api/v2). The
    // integration tests (V2ConventionsTests) check the same through HTTP
    public class ErrorResponseTests
    {
        private static readonly DateTime RetryAt = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        public sealed class InheritedController : ApiController
        {
            public IActionResult Handle(Result result) => HandleResult(result);
        }

        public sealed class V2Controller : V2ApiController
        {
            public IActionResult Handle(Result result) => HandleResult(result);
        }

        private static T Create<T>() where T : ControllerBase, new()
            => new()
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
                ProblemDetailsFactory = new TestProblemDetailsFactory(),
            };

        private static ObjectResult Inherited(params Error[] errors) => (ObjectResult)Create<InheritedController>().Handle(Result.Fail(errors));

        private static ObjectResult V2(params Error[] errors) => (ObjectResult)Create<V2Controller>().Handle(Result.Fail(errors));

        private static ProblemDetails ShapeA(ObjectResult result, int status, string code)
        {
            Assert.Equal(status, result.StatusCode);
            var problem = Assert.IsType<ProblemDetails>(result.Value); // exactly ProblemDetails: no "errors" member
            Assert.Equal(status, problem.Status);
            Assert.Equal(code, problem.Title);
            return problem;
        }

        // ── Inherited routes keep §3 ─────────────────────────────────────────

        [Fact]
        public void Inherited_CodedValidationError_IsShapeC()
        {
            var result = Inherited(Error.Validation("Appointment.SlotInPast", "The slot has started"));

            Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
            var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
            Assert.Equal(["The slot has started"], problem.Errors["Appointment.SlotInPast"]);
        }

        [Theory]
        [InlineData(ErrorType.InvalidCredentials, 401)]
        [InlineData(ErrorType.Unauthorized, 401)]
        [InlineData(ErrorType.Forbidden, 403)]
        [InlineData(ErrorType.NotFound, 404)]
        [InlineData(ErrorType.Conflict, 409)]
        [InlineData(ErrorType.TooManyRequests, 429)]
        [InlineData(ErrorType.Failure, 500)]
        public void Inherited_OtherErrors_AreShapeAWithTheirStatus(ErrorType type, int status)
            => ShapeA(Inherited(new Error("X.Reason", "Text", type)), status, "X.Reason");

        [Fact]
        public void Inherited_ErrorMetadata_BecomesExtensions()
        {
            var problem = ShapeA(Inherited(Error.TooManyRequests("Auth.AccountLockedOut", "Locked").WithRetryAt(RetryAt)), 429, "Auth.AccountLockedOut");

            Assert.Equal(RetryAt, problem.Extensions[ErrorMetadata.RetryAt]);
        }

        // ── /api/v2 (§13.1) ──────────────────────────────────────────────────

        [Fact]
        public void V2_CodedValidationError_IsShapeA()
        {
            var problem = ShapeA(V2(Error.Validation("Consent.Required", "Accept the terms")), 400, "Consent.Required");

            Assert.Equal("Accept the terms", problem.Detail);
        }

        [Fact]
        public void V2_SeveralValidationErrors_TitleIsTheFirstCode_DetailJoinsTheMessages()
        {
            var problem = ShapeA(
                V2(Error.Validation("Auth.RegistrationFailed", "Too short."), Error.Validation("Auth.RegistrationFailed", "Needs a digit.")),
                400, "Auth.RegistrationFailed");

            Assert.Equal("Too short. Needs a digit.", problem.Detail);
        }

        [Theory]
        [InlineData(ErrorType.Validation, 400)]
        [InlineData(ErrorType.InvalidCredentials, 400)] // 401 only means the token (§13.1 "401")
        [InlineData(ErrorType.Unauthorized, 401)]
        [InlineData(ErrorType.Forbidden, 403)]
        [InlineData(ErrorType.NotFound, 404)]
        [InlineData(ErrorType.Conflict, 409)]
        [InlineData(ErrorType.TooManyRequests, 429)]
        [InlineData(ErrorType.Failure, 500)]
        public void V2_EveryCodedError_IsShapeAWithTheCodeAsTitle(ErrorType type, int status)
            => ShapeA(V2(new Error("X.Reason", "Text", type)), status, "X.Reason");

        [Fact]
        public void V2_ErrorMetadata_BecomesExtensions()
        {
            var problem = ShapeA(V2(Error.Validation("Otp.InvalidCode", "Wrong code").WithAttemptsLeft(2)), 400, "Otp.InvalidCode");

            Assert.Equal(2, problem.Extensions[ErrorMetadata.AttemptsLeft]);
        }

        [Fact]
        public void V2_TooManyRequests_CarriesRetryAt()
        {
            var problem = ShapeA(V2(Error.TooManyRequests("Otp.ResendTooSoon", "Wait").WithRetryAt(RetryAt)), 429, "Otp.ResendTooSoon");

            Assert.Equal(RetryAt, problem.Extensions[ErrorMetadata.RetryAt]);
        }

        [Fact]
        public void V2ApiController_RoutesUnderApiV2_AndMarksItsActions()
        {
            Assert.Equal("api/v2", V2ApiController.RoutePrefix);
            // MVC takes the routes of the nearest class that declares any, so this one replaces ApiController's "api/[controller]"
            Assert.Equal("api/v2/[controller]", typeof(V2ApiController).GetCustomAttribute<RouteAttribute>(inherit: false)!.Template);
            // Inherited, so every V2 controller's endpoints carry it (HttpContext.IsApiV2)
            Assert.NotNull(typeof(V2Controller).GetCustomAttribute<ApiV2Attribute>(inherit: true));
            Assert.Null(typeof(InheritedController).GetCustomAttribute<ApiV2Attribute>(inherit: true));
        }
    }
}
