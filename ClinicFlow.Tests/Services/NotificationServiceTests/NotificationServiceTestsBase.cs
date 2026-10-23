using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public abstract class NotificationServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used because NotificationProfile maps Type through a string
        protected readonly IUnitOfWork _uow;
        protected readonly ILogger<NotificationService> _logger;
        protected readonly IMapper _mapper;
        protected readonly IClock _clock;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

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

            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _sut = new NotificationService(_uow, _logger, _mapper, _clock);
        }
    }
}
