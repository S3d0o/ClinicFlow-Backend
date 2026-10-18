using AutoMapper;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.ReviewServiceTests
{
    public abstract class ReviewServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used so ReviewResponse.PatientName is covered too
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<ReviewService> _logger;
        protected readonly IDbContextTransaction _transaction;

        // System Under Test
        protected readonly ReviewService _sut;

        protected ReviewServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _transaction = Substitute.For<IDbContextTransaction>();
            _uow.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_transaction);
            _logger = Substitute.For<ILogger<ReviewService>>();
            _mapper = new ServiceCollection()
                .AddLogging()
                .AddAutoMapper(cfg => { }, typeof(ServiceAssemblyReference).Assembly)
                .BuildServiceProvider()
                .GetRequiredService<IMapper>();

            _sut = new ReviewService(_uow, _mapper, _logger);
        }
    }
}
