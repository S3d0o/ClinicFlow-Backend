using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.PatientServiceTests
{
    public abstract class PatientServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used because the update relies on its null-skipping rule
        protected readonly IUnitOfWork _uow;
        protected readonly ILogger<PatientService> _logger;
        protected readonly IMapper _mapper;

        // System Under Test
        protected readonly PatientService _sut;

        protected PatientServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<PatientService>>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _sut = new PatientService(_uow, _logger, _mapper);
        }
    }
}
