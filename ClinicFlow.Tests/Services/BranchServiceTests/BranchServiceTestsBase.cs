using AutoMapper;
using Domain.Entities.Tenancy;
using Domain.Enums;
using Domain.Interfaces;
using Domain.ResultPattern;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Authorization;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public abstract class BranchServiceTestsBase
    {
        protected const int BranchId = 7;
        protected const int RoomId = 31;

        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly IAccessGuard _accessGuard;
        protected readonly ILogger<BranchService> _logger;
        protected readonly ITransaction _transaction;

        protected readonly BranchService _sut;

        protected BranchServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _accessGuard = Substitute.For<IAccessGuard>();
            _logger = Substitute.For<ILogger<BranchService>>();
            _transaction = Substitute.For<ITransaction>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _uow.RunsTransactionsWith<Result<BranchResponse>>(_transaction);
            _uow.RunsTransactionsWith<Result<RoomResponse>>(_transaction);

            // No branch has hours yet unless a test says otherwise: a new branch opens around the clock
            _uow.WorkingHours.GetUsualWeekAsync(Arg.Any<CancellationToken>()).Returns(new List<BranchWorkingHours>());

            // An organization-wide admin unless a test narrows it
            _accessGuard.ScopeOf(Arg.Any<string>()).Returns(PermissionScope.Organization);
            _accessGuard.CanAccessAsync(Arg.Any<string>(), Arg.Any<AccessTarget>(), Arg.Any<CancellationToken>()).Returns(true);

            _sut = new BranchService(_uow, _mapper, _accessGuard, _logger);
        }

        protected static BranchRequest ARequest(string code = "NSR")
            => new(code, "Nasr City", "مدينة نصر", "12 Makram Ebeid", "١٢ مكرم عبيد", "01001234567", "Africa/Cairo");

        protected static Branch ABranch(int id = BranchId, bool isActive = true)
            => new() { Id = id, Code = "HLP", NameEn = "Heliopolis", NameAr = "مصر الجديدة", TimeZoneId = "Africa/Cairo", IsActive = isActive };

        protected static RoomRequest ARoomRequest(bool? isActive = null)
            => new("Room 1", "غرفة ١", RoomKind.Consultation, isActive);

        protected static Room ARoom()
            => new() { Id = RoomId, BranchId = BranchId, NameEn = "Old", NameAr = "قديمة", Kind = RoomKind.Triage, IsActive = true };
    }
}
