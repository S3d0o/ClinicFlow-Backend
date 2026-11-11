using Domain.Entities.IdentityModule;
using Domain.Entities.Tenancy;
using Domain.Enums;
using Shared.Messaging;

namespace Services.Messaging
{
    // The placeholder values of each kind of message (MessageTemplateCatalog), written for the recipient's language.
    // clinicName is added by TemplatedMessenger
    public static class MessageValues
    {
        public static string FullName(ApplicationUser user) => $"{user.FirstName} {user.LastName}".Trim();

        // A message about the user's account, with its one link
        public static Dictionary<string, string> Account(ApplicationUser user, string linkPlaceholder, string url) => new()
        {
            [TemplatePlaceholders.Name] = FullName(user),
            [linkPlaceholder] = url,
        };

        // The date and time are the branch's wall clock, as the patient reads them (docs/v2/adr/0003)
        public static Dictionary<string, string> Appointment(Appointment appointment, Branch? branch, Language language) => new()
        {
            [TemplatePlaceholders.PatientName] = FullName(appointment.Patient.User),
            [TemplatePlaceholders.DoctorName] = FullName(appointment.Doctor.User),
            [TemplatePlaceholders.Date] = MessageFormats.Date(appointment.Slot.Date, language),
            [TemplatePlaceholders.Time] = MessageFormats.Time(appointment.Slot.StartTime, language),
            [TemplatePlaceholders.BranchName] = (language == Language.En ? branch?.NameEn : branch?.NameAr) ?? "",
        };
    }
}
