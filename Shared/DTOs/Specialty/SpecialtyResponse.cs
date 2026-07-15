using System;
using System.Collections.Generic;
using System.Text;

namespace Shared.DTOs.Specialty
{
    public record SpecialtyResponse(
    int Id,
    string Name,
    string? Description,
    string? IconUrl,
    bool IsActive);
}
