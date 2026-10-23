using AutoMapper;
using Domain.Entities.AppModule;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public abstract class DoctorServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used so mapping rules are covered too
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<DoctorService> _logger;
        protected readonly IClock _clock;
        protected readonly ITransaction _transaction = Substitute.For<ITransaction>();

        // Clinic-local "now" for every test: Sunday 18 Oct 2026, 08:00
        protected static readonly DateTime ClinicNow = new(2026, 10, 18, 8, 0, 0);
        protected static DateOnly ClinicToday => DateOnly.FromDateTime(ClinicNow);

        // Slots passed to uow.Slots.AddRangeAsync (null when generation was skipped)
        protected List<AppointmentSlot>? _generatedSlots;

        // System Under Test
        protected readonly DoctorService _sut;

        protected DoctorServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _uow.RunsTransactionsWith<int>(_transaction);  // UpdateScheduleAsync: the regenerated slot count
            _uow.RunsTransactionsWith<bool>(_transaction); // DeleteScheduleAsync: whether the schedule was kept
            _logger = Substitute.For<ILogger<DoctorService>>();
            _clock = Substitute.For<IClock>();
            _clock.ClinicNow.Returns(ClinicNow);
            _clock.ClinicToday.Returns(ClinicToday);
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _uow.Slots.AddRangeAsync(
                    Arg.Do<IEnumerable<AppointmentSlot>>(slots => _generatedSlots = slots.ToList()),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // No existing slots unless a test says otherwise
            _uow.Slots.GetSlotTimesAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new List<(DateOnly, TimeOnly, TimeOnly)>());

            _sut = new DoctorService(_uow, _mapper, _logger, _clock);
        }
    }
}
