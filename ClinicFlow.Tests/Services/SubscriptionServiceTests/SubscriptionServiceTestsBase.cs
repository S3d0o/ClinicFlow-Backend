using Domain.Entities.Subscriptions;
using Domain.Interfaces;
using Domain.Interfaces.IRepositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Services.Implementations;
using Shared.Authorization;
using Shared.Settings;

namespace ClinicFlow.Tests.Services.SubscriptionServiceTests
{
    public abstract class SubscriptionServiceTestsBase
    {
        // Dependencies
        protected readonly IUnitOfWork _uow;
        protected readonly ISubscriptionRepo _subscriptions;
        protected readonly PlatformSettings _settings = new() { AllowPlanSwitching = true };
        protected readonly ILogger<SubscriptionService> _logger;

        // System Under Test
        protected readonly SubscriptionService _sut;

        // The seeded plans (PlanConfig): Starter has no add-ons
        protected static readonly Plan Starter = PlanWith(1, "Starter");
        protected static readonly Plan Clinic = PlanWith(2, "Clinic", Modules.FrontDesk, Modules.Billing, Modules.PatientPortal, Modules.Reporting);
        protected static readonly Plan Full = PlanWith(3, "Full",
            Modules.FrontDesk, Modules.Billing, Modules.PatientPortal, Modules.Reporting, Modules.ClinicalRecords, Modules.Insurance);

        protected readonly List<OrganizationModuleOverride> _overrides = [];

        protected SubscriptionServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _subscriptions = Substitute.For<ISubscriptionRepo>();
            _uow.Subscriptions.Returns(_subscriptions);
            _subscriptions.GetOverridesAsync(Arg.Any<CancellationToken>()).Returns(_ => _overrides);
            _logger = Substitute.For<ILogger<SubscriptionService>>();

            _sut = new SubscriptionService(_uow, Options.Create(_settings), _logger);
        }

        protected OrganizationSubscription OnPlan(Plan plan)
        {
            var subscription = new OrganizationSubscription { OrganizationId = 1, PlanId = plan.Id, Plan = plan };
            _subscriptions.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(subscription);
            return subscription;
        }

        protected OrganizationModuleOverride WithOverride(string module, bool enabled)
        {
            var moduleOverride = new OrganizationModuleOverride { OrganizationId = 1, ModuleCode = module, Enabled = enabled };
            _overrides.Add(moduleOverride);
            return moduleOverride;
        }

        private static Plan PlanWith(int id, string code, params string[] modules) => new()
        {
            Id = id,
            Code = code,
            Name = code,
            Modules = modules.Select(m => new PlanModule { PlanId = id, ModuleCode = m }).ToList(),
        };
    }
}
