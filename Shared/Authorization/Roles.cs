namespace Shared.Authorization
{
    // The role names (docs/v2/02 §2). Only Admin, Doctor and Patient exist as Identity roles until P1 adds staff
    public static class Roles
    {
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Receptionist = "Receptionist";
        public const string Nurse = "Nurse";
        public const string Doctor = "Doctor";
        public const string Accountant = "Accountant";
        public const string Patient = "Patient";

        public static readonly IReadOnlyList<string> All = [Admin, Manager, Receptionist, Nurse, Doctor, Accountant, Patient];

        // For [Authorize(Roles = AnyStaff)] on reads every staff member may use (the contract's "Any staff"), such as
        // the branch list and the organization's settings: every role but Patient
        public const string AnyStaff = Admin + "," + Manager + "," + Receptionist + "," + Nurse + "," + Doctor + "," + Accountant;
    }
}
