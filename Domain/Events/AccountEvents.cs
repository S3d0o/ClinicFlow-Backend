namespace Domain.Events
{
    // The handlers create the email tokens when they send, so no token is ever stored in the outbox
    public record EmailConfirmationRequested(Guid UserId) : IDomainEvent;
    public record PasswordResetRequested(Guid UserId) : IDomainEvent;

    public record DoctorApproved(Guid DoctorUserId) : IDomainEvent;
    public record DoctorSuspended(Guid DoctorUserId) : IDomainEvent;
}
