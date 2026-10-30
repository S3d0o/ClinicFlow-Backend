using Shared.DTOs.Common;

namespace Presentation.Validators
{
    // The one paging rule of /api/v2 (contract §13.1 "Lists"). It also validates every filter derived from PageRequest:
    // ValidationFilter runs the validators of an argument's base types too, so derived validators don't Include it
    public class PageRequestValidator : AbstractValidator<PageRequest>
    {
        public PageRequestValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThanOrEqualTo(1).WithMessage("pageNumber must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, PageRequest.MaxPageSize)
                .WithMessage($"pageSize must be between 1 and {PageRequest.MaxPageSize}.");
        }
    }
}
