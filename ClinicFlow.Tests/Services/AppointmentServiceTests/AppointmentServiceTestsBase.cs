using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public abstract class AppointmentServiceTestsBase
    {
        // Dependencies
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<AppointmentService> _logger;
        protected readonly INotificationService _notificationService;
        protected readonly IEmailService _emailService;
        protected readonly IDateTimeProvider _dateTimeProvider;

        // System Under Test
        protected readonly AppointmentService _sut;

        protected AppointmentServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _mapper = Substitute.For<IMapper>();
            _logger = Substitute.For<ILogger<AppointmentService>>();
            _notificationService = Substitute.For<INotificationService>();
            _emailService = Substitute.For<IEmailService>();
            _dateTimeProvider = Substitute.For<IDateTimeProvider>();

            _sut = new AppointmentService(
                _uow,
                _mapper,
                _logger,
                _notificationService,
                _emailService,
                _dateTimeProvider
            );
        }
    }
}
