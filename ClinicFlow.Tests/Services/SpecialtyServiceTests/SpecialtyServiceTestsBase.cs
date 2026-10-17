using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.SpecialtyServiceTests
{
    public abstract class SpecialtyServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used so request/response mapping is covered too
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<SpecialtyService> _logger;

        // System Under Test
        protected readonly SpecialtyService _sut;

        protected SpecialtyServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<SpecialtyService>>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _sut = new SpecialtyService(_uow, _mapper, _logger);
        }
    }
}
