using AutoMapper;
using Domain.Entities.AppModule;
using Shared.DTOs.Specialty;

namespace Services.MappingProfiles
{
    internal class SpecialtyProfile : Profile
    {
        public SpecialtyProfile()
        {
            CreateMap<SpecialtyRequest, Specialty>();
            CreateMap<Specialty, SpecialtyResponse>();
        }
    }
}
