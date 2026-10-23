using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.AppointmentBookingServiceTests
{
    public abstract class AppointmentBookingServiceTestsBase
    {
        // Dependencies
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<AppointmentBookingService> _logger;
        protected readonly INotificationService _notificationService;
        protected readonly IEmailService _emailService;
        protected readonly IClock _clock;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        // System Under Test
        protected readonly AppointmentBookingService _sut;

        protected AppointmentBookingServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _mapper = Substitute.For<IMapper>();
            _logger = Substitute.For<ILogger<AppointmentBookingService>>();
            _notificationService = Substitute.For<INotificationService>();
            _emailService = Substitute.For<IEmailService>();
            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _sut = new AppointmentBookingService(
                _uow,
                _mapper,
                _logger,
                _notificationService,
                _emailService,
                _clock
            );
        }
    }
}
