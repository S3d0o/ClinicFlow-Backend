using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Entities.Tenancy;
using Domain.Enums;
using Domain.Interfaces;
using NSubstitute;
using Services.Abstraction.Contracts;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    // Shared by the outbox handler tests: a unit of work whose MessageDeliveries says nothing was delivered yet,
    // the notifications the handler adds, and the templated emails it sends
    public abstract class EventHandlerTestsBase
    {
        protected const long MessageId = 77;
        protected const int AppointmentId = 9;
        protected const int BranchId = 4;
        protected static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        protected readonly CancellationToken _ct = CancellationToken.None;

        protected readonly IUnitOfWork _uow;
        protected readonly ITemplatedMessenger _messenger;
        protected readonly IClock _clock;
        protected readonly List<Notification> _added = [];
        protected readonly List<(ApplicationUser To, string TemplateKey, IReadOnlyDictionary<string, string> Values)> _emails = [];

        protected readonly Guid _patientUserId = Guid.NewGuid();
        protected readonly Guid _doctorUserId = Guid.NewGuid();

        protected EventHandlerTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _messenger = Substitute.For<ITemplatedMessenger>();
            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(UtcNow);

            _uow.Notifications.AddRangeAsync(Arg.Do<IEnumerable<Notification>>(n => _added.AddRange(n)), Arg.Any<CancellationToken>());
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>())
                .Returns(new Branch { Id = BranchId, NameEn = "Maadi", NameAr = "المعادي" });
            _messenger.When(m => m.SendEmailAsync(
                    Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>()))
                .Do(call => _emails.Add((call.ArgAt<ApplicationUser>(0), call.ArgAt<string>(1), call.ArgAt<IReadOnlyDictionary<string, string>>(2))));
        }

        // A confirmed 10:00 visit on 25 Oct at the branch, with the names the messages use. The patient reads English
        // and has a confirmed email
        protected Appointment StubAppointment()
        {
            var appointment = new Appointment
            {
                Id = AppointmentId,
                BranchId = BranchId,
                Slot = new AppointmentSlot { Date = new DateOnly(2026, 10, 25), StartTime = new TimeOnly(10, 0) },
                Patient = new PatientProfile
                {
                    UserId = _patientUserId,
                    User = new ApplicationUser
                    {
                        Id = _patientUserId, FirstName = "Mona", LastName = "Hassan", Email = "mona@test.com", EmailConfirmed = true,
                        PreferredLanguage = Language.En,
                    },
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

        protected Task NoEmailWasSent()
            => _messenger.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default!, default!, default);
    }
}
