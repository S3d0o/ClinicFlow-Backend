using Services.Abstraction.Contracts;
using Shared.DTOs.Specialty;

namespace Presentation.Controllers
{
    public class SpecialtyController(ISpecialtyService service) : ApiController
    {
        [HttpGet]
        [EndpointSummary("Retrieve all specialties")]
        [EndpointDescription("Returns all active specialties. Admin can see inactive ones by passing includeInactive=true.")]
        [ProducesResponseType(typeof(List<SpecialtyResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<List<SpecialtyResponse>>> GetAll(
            CancellationToken ct,
            [FromQuery] bool includeInactive = false)
        {
            if (includeInactive && !User.IsInRole("Admin"))
                return Forbid();

            return HandleResult(await service.GetAllAsync(includeInactive, ct));
        }

        [HttpGet("{id}")]
        [EndpointSummary("Get specialty by ID")]
        [EndpointDescription("Returns a single active specialty by its ID.")]
        [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SpecialtyResponse>> GetById(int id, CancellationToken ct)
            => HandleResult(await service.GetByIdAsync(id, ct));

        [HttpPost]
        [EndpointSummary("Create a new specialty")]
        [EndpointDescription("Admin only. Creates a new specialty. Name must be unique.")]
        [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        //[Authorize(Roles = "Admin")]
        public async Task<ActionResult<SpecialtyResponse>> Create([FromBody] SpecialtyRequest dto, CancellationToken ct)
        {
            var result = await service.CreateAsync(dto, ct);
            return HandleResult(result);
        }

        [HttpPut("{id}")]
        [EndpointSummary("Update an existing specialty")]
        [EndpointDescription("Admin only. Updates name, description, or icon of an existing specialty.")]
        [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        //[Authorize(Roles = "Admin")]
        public async Task<ActionResult<SpecialtyResponse>> Update(
            [FromRoute] int id,
            [FromBody] SpecialtyRequest dto,
            CancellationToken ct)
            => HandleResult(await service.UpdateAsync(id, dto, ct));

        [HttpDelete("{id}")]
        [EndpointSummary("Delete a specialty")]
        [EndpointDescription("Admin only. Hard deletes a specialty only if no doctors are assigned to it.")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        //[Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => HandleResult(await service.DeleteByIdAsync(id, ct));
    }


}
