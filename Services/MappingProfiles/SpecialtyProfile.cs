using AutoMapper;
using Domain.Entities.AppModule;
using Shared.DTOs.Specialty;

namespace Services.MappingProfiles
{
    internal class SpecialtyProfile : Profile
    {
        public SpecialtyProfile()
        {
            // IsActive is optional: null leaves the entity's value (true for a new specialty) unchanged (REVIEW.md L6)
            CreateMap<SpecialtyRequest, Specialty>()
                .ForMember(d => d.IsActive, o =>
                {
                    o.PreCondition(s => s.IsActive.HasValue);
                    o.MapFrom(s => s.IsActive!.Value);
                });
            CreateMap<Specialty, SpecialtyResponse>();
        }
    }
}
