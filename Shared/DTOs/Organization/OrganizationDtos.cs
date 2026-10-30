using Domain.Enums;

namespace Shared.DTOs.Organization
{
    // GET/PUT /api/v2/organization (API_CONTRACT.md §13.24): shown on invoices, receipts and prescriptions (FR-ORG-01)
    public sealed record OrganizationProfileRequest(
        string LegalName,
        string BrandNameEn,
        string BrandNameAr,
        string TaxNumber,
        string CommercialRegistration,
        string Phone,
        string Email);

    public sealed record OrganizationProfileResponse(
        string LegalName,
        string BrandNameEn,
        string BrandNameAr,
        string TaxNumber,
        string CommercialRegistration,
        string Phone,
        string Email);

    // POST /api/v2/branches and PUT /api/v2/branches/{id} (§13.25)
    public sealed record BranchRequest(
        string Code,
        string NameEn,
        string NameAr,
        string AddressEn,
        string AddressAr,
        string Phone,
        string TimeZone);

    // The contract's Branch (§13.2)
    public sealed record BranchResponse(
        int Id,
        string Code,
        string NameEn,
        string NameAr,
        string AddressEn,
        string AddressAr,
        string Phone,
        string TimeZone,
        bool IsActive);

    // POST /api/v2/branches/{id}/rooms and PUT /api/v2/rooms/{id}; IsActive is read on PUT only
    public sealed record RoomRequest(string NameEn, string NameAr, RoomKind Kind, bool? IsActive = null);

    public sealed record RoomResponse(int Id, int BranchId, string NameEn, string NameAr, RoomKind Kind, bool IsActive);
}
