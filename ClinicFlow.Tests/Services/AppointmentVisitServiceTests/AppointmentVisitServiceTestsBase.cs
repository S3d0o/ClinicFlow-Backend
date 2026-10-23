using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.AppointmentVisitServiceTests
{
    public abstract class AppointmentVisitServiceTestsBase
    {
        // Dependencies
        protected readonly IUnitOfWork _uow;
        protected readonly ILogger<AppointmentVisitService> _logger;
        protected readonly INotificationService _notificationService;
        protected readonly IClock _clock;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        // System Under Test
        protected readonly AppointmentVisitService _sut;

        protected AppointmentVisitServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<AppointmentVisitService>>();
            _notificationService = Substitute.For<INotificationService>();
            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _sut = new AppointmentVisitService(_uow, _logger, _notificationService, _clock);
        }
    }
}
