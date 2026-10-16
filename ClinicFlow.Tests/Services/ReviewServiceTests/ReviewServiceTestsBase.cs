using AutoMapper;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.ReviewServiceTests
{
    public abstract class ReviewServiceTestsBase
    {
        // Dependencies
        protected readonly IUnitOfWork _uow;
        protected readonly IMapper _mapper;
        protected readonly ILogger<ReviewService> _logger;

        // System Under Test
        protected readonly ReviewService _sut;

        protected ReviewServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _mapper = Substitute.For<IMapper>();
            _logger = Substitute.For<ILogger<ReviewService>>();

            _sut = new ReviewService(_uow, _mapper, _logger);
        }
    }
}
