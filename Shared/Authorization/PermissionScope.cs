namespace Shared.Authorization
{
    // How far a granted permission reaches (docs/v2/02 §4). Ordered, so the widest of several grants is the largest
    public enum PermissionScope
    {
        Own = 1,          // records the user owns
        Branch = 2,       // records of the branches the user is assigned to
        Organization = 3, // every record of the organization
    }
}
