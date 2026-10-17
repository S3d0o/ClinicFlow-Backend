using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public abstract class NotificationServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used because NotificationProfile maps Type through a string
        protected readonly IUnitOfWork _uow;
        protected readonly ILogger<NotificationService> _logger;
        protected readonly IMapper _mapper;

        // System Under Test
        protected readonly NotificationService _sut;

        protected NotificationServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<NotificationService>>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _sut = new NotificationService(_uow, _logger, _mapper);
        }
    }
}
