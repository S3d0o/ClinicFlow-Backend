using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public abstract class AdminServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used so DoctorResponse names are covered too
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<AdminService> _logger;
        protected readonly INotificationService _notificationService;
        protected readonly IClock _clock;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        // System Under Test
        protected readonly AdminService _sut;

        protected AdminServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<AdminService>>();
            _notificationService = Substitute.For<INotificationService>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _sut = new AdminService(_uow, _mapper, _logger, _notificationService, _clock);
        }
    }
}
