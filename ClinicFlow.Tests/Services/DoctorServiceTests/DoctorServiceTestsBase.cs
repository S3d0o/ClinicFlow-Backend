using AutoMapper;
using Domain.Entities.AppModule;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public abstract class DoctorServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used so mapping rules are covered too
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<DoctorService> _logger;

        // Slots passed to uow.Slots.AddRangeAsync (null when generation was skipped)
        protected List<AppointmentSlot>? _generatedSlots;

        // System Under Test
        protected readonly DoctorService _sut;

        protected DoctorServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _logger = Substitute.For<ILogger<DoctorService>>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _uow.Slots.AddRangeAsync(
                    Arg.Do<IEnumerable<AppointmentSlot>>(slots => _generatedSlots = slots.ToList()),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            _sut = new DoctorService(_uow, _mapper, _logger);
        }
    }
}
