using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.OrganizationServiceTests
{
    public abstract class OrganizationServiceTestsBase
    {
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<OrganizationService> _logger;

        protected readonly OrganizationService _sut;

        protected OrganizationServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<OrganizationService>>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _sut = new OrganizationService(_uow, _mapper, _logger);
        }
    }
}
