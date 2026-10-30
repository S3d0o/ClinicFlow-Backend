using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Authorization;
using Shared.DTOs.Entitlements;

namespace ClinicFlow.Tests.Services.EntitlementsTests
{
    public abstract class EntitlementsTestsBase
    {
        protected const int OrganizationId = 1;

        // Dependencies; the cache is the real one, so caching is part of what is tested
        protected readonly ITenantContext _tenant;
        protected readonly IEntitlementQueries _queries;
        protected readonly IClock _clock;
        protected readonly EntitlementCache _cache;
        protected readonly ILogger<Entitlements> _logger;

        // System Under Test
        protected readonly Entitlements _sut;

        protected EntitlementsTestsBase()
        {
            _tenant = Substitute.For<ITenantContext>();
            _tenant.OrganizationId.Returns(OrganizationId);
            _queries = Substitute.For<IEntitlementQueries>();
            _clock = Substitute.For<IClock>();
            _clock.UtcNow.Returns(new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc));
            _cache = new EntitlementCache(_clock);
            _logger = Substitute.For<ILogger<Entitlements>>();

            _sut = new Entitlements(_tenant, _queries, _cache, _logger);
        }

        // The Clinic plan as seeded (FD, BIL, PAT, RPT), with two of its limits
        protected void OnClinicPlan(params ModuleOverrideData[] overrides)
            => _queries.GetCurrentOrganizationAsync(Arg.Any<CancellationToken>()).Returns(new OrganizationEntitlementsData(
                "Clinic",
                [Modules.FrontDesk, Modules.Billing, Modules.PatientPortal, Modules.Reporting],
                new Dictionary<string, int> { [Limits.Branches] = 1, [Limits.Doctors] = 5 },
                overrides));
    }
}
