namespace Domain.Entities.Tenancy
{
    // Data the clinic creates (schedules, slots, appointments, reviews, notifications, ...) belongs to one organization.
    // ClinicDbContext filters every query to the current organization, and a save interceptor fills in OrganizationId
    // and rejects rows of another organization (docs/v2/adr/0001). People and reference data stay global.
    public interface IOrganizationOwned
    {
        int OrganizationId { get; set; }
    }
}
