using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using Domain.Interfaces;
using NSubstitute;
using Services.Abstraction.Contracts;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    // Shared by the outbox handler tests: a unit of work whose MessageDeliveries says nothing was delivered yet,
    // and the notifications the handler adds
    public abstract class EventHandlerTestsBase
    {
        protected const long MessageId = 77;
        protected const int AppointmentId = 9;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        protected readonly CancellationToken _ct = CancellationToken.None;

        protected readonly IUnitOfWork _uow;
        protected readonly IEmailService _emailService;
        protected readonly IClock _clock;
        protected readonly List<Notification> _added = [];

        protected readonly Guid _patientUserId = Guid.NewGuid();
        protected readonly Guid _doctorUserId = Guid.NewGuid();

        protected EventHandlerTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _emailService = Substitute.For<IEmailService>();
            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _uow.Notifications.AddRangeAsync(Arg.Do<IEnumerable<Notification>>(n => _added.AddRange(n)), Arg.Any<CancellationToken>());
        }

        // A confirmed 10:00 visit on 25 Oct, with the names the messages use
        protected Appointment StubAppointment()
        {
            var appointment = new Appointment
            {
                Id = AppointmentId,
                Slot = new AppointmentSlot { Date = new DateOnly(2026, 10, 25), StartTime = new TimeOnly(10, 0) },
                Patient = new PatientProfile
                {
                    UserId = _patientUserId,
                    User = new ApplicationUser { Id = _patientUserId, FirstName = "Mona", LastName = "Hassan", Email = "mona@test.com" },
                },
                Doctor = new DoctorProfile
                {
                    UserId = _doctorUserId,
                    User = new ApplicationUser { Id = _doctorUserId, FirstName = "Ahmed", LastName = "Kamal" },
                },
            }.WithStatus(AppointmentStatus.Confirmed);
            _uow.Appointments.GetByIdAsync(AppointmentId, _ct).Returns(appointment);
            return appointment;
        }

        protected void AlreadyDelivered(string channel)
            => _uow.MessageDeliveries.WasDeliveredAsync(MessageId, channel, _ct).Returns(true);
    }
}
