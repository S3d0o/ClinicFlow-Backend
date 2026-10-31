using Domain.Entities.Tenancy;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.OrganizationSettingsServiceTests
{
    public abstract class OrganizationSettingsServiceTestsBase
    {
        protected const int OrganizationId = 1;
        protected static readonly DateTime Now = new(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc);

        protected readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
        protected readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
        protected readonly IClock _clock = Substitute.For<IClock>();
        protected readonly ILogger<OrganizationSettingsService> _logger = Substitute.For<ILogger<OrganizationSettingsService>>();
        protected readonly ITransaction _transaction = Substitute.For<ITransaction>();
        protected readonly OrganizationSettingsCache _cache;

        // What the database holds; GetAllAsync returns copies, GetForUpdateAsync the tracked rows themselves
        protected readonly List<OrganizationSetting> _stored = [];

        protected readonly OrganizationSettingsService _sut;

        protected OrganizationSettingsServiceTestsBase()
        {
            _tenant.OrganizationId.Returns(OrganizationId);
            _clock.UtcNow.Returns(Now);
            _cache = new OrganizationSettingsCache(_clock);

            _uow.RunsTransactionsWith<(OrganizationSettingsResponse, List<string>)>(_transaction);
            _uow.OrganizationSettings.GetAllAsync(Arg.Any<CancellationToken>())
                .Returns(_ => _stored.Select(s => new OrganizationSetting { Id = s.Id, Key = s.Key, Value = s.Value }).ToList());
            _uow.OrganizationSettings.GetForUpdateAsync(Arg.Any<CancellationToken>()).Returns(_ => _stored.ToList());
            _uow.OrganizationSettings.When(r => r.Add(Arg.Any<OrganizationSetting>())).Do(call => _stored.Add(call.Arg<OrganizationSetting>()!));

            _sut = new OrganizationSettingsService(_uow, _tenant, _cache, _logger);
        }

        protected void Store(string key, string value) => _stored.Add(new OrganizationSetting { Id = _stored.Count + 1, Key = key, Value = value });
    }
}
