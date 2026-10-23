using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services;
using Services.Implementations;
using Shared.DTOs.Review;
using Shared.ResultPattern;

namespace ClinicFlow.Tests.Services.ReviewServiceTests
{
    public abstract class ReviewServiceTestsBase
    {
        // Dependencies — the real AutoMapper configuration is used so ReviewResponse.PatientName is covered too
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<ReviewService> _logger;
        protected readonly ITransaction _transaction;

        // System Under Test
        protected readonly ReviewService _sut;

        protected ReviewServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _transaction = Substitute.For<ITransaction>();
            _uow.RunsTransactionsWith<Result<ReviewResponse>>(_transaction);
            _uow.RunsTransactionsWith<Result>(_transaction);
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
