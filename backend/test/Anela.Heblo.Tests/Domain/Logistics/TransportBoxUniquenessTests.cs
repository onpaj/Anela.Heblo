using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Transport.Contracts;
using Anela.Heblo.Application.Features.Logistics.Transport.Handlers;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class TransportBoxUniquenessTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TransportBoxRepository _repository;
    private readonly ChangeTransportBoxStateHandler _handler;
    private readonly Mock<ICurrentUserService> _mockUserService;
    private readonly Mock<IMediator> _mockMediator;
    
    private const string TestUser = "TestUser";

    public TransportBoxUniquenessTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _dbContext.Database.EnsureCreated();
        
        _repository = new TransportBoxRepository(_dbContext, NullLogger<TransportBoxRepository>.Instance);
        _mockUserService = new Mock<ICurrentUserService>();
        _mockMediator = new Mock<IMediator>();
        
        _mockUserService.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Guid.NewGuid().ToString(), TestUser, "test@test.com", true));
            
        _handler = new ChangeTransportBoxStateHandler(
            _repository, 
            _mockMediator.Object, 
            NullLogger<ChangeTransportBoxStateHandler>.Instance, 
            _mockUserService.Object, 
            TimeProvider.System);
    }

    [Fact]
    public async Task OpenTwoTransportBoxesWithSameCode_ShouldPreventDuplicate()
    {
        // Arrange - Vytvořit první box a otevřít s kódem B001
        var firstBox = new TransportBox();
        await _repository.AddAsync(firstBox);
        await _repository.SaveChangesAsync();

        var firstOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        var firstResult = await _handler.Handle(firstOpenRequest, CancellationToken.None);
        firstResult.Success.Should().BeTrue();

        // Act - Pokusit se otevřít druhý box se stejným kódem B001
        var secondBox = new TransportBox();
        await _repository.AddAsync(secondBox);
        await _repository.SaveChangesAsync();

        var secondOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = secondBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        var secondResult = await _handler.Handle(secondOpenRequest, CancellationToken.None);

        // Assert - Druhý pokus by měl selhat
        secondResult.Success.Should().BeFalse();
        secondResult.ErrorMessage.Should().Contain("Transport box with code 'B001' is already active");
    }

    [Fact]
    public async Task OpenTwoTransportBoxesWithSameCodeCaseInsensitive_ShouldPreventDuplicate()
    {
        // Arrange - Vytvořit první box s kódem B001 (uppercase)
        var firstBox = new TransportBox();
        await _repository.AddAsync(firstBox);
        await _repository.SaveChangesAsync();

        var firstOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        var firstResult = await _handler.Handle(firstOpenRequest, CancellationToken.None);
        firstResult.Success.Should().BeTrue();

        // Act - Pokusit se otevřít druhý box s kódem b001 (lowercase)
        var secondBox = new TransportBox();
        await _repository.AddAsync(secondBox);
        await _repository.SaveChangesAsync();

        var secondOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = secondBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "b001" // lowercase
        };

        var secondResult = await _handler.Handle(secondOpenRequest, CancellationToken.None);

        // Assert - Mělo by selhat kvůli case-insensitive duplicitě
        secondResult.Success.Should().BeFalse();
        secondResult.ErrorMessage.Should().Contain("Transport box with code 'B001' is already active");
    }

    [Fact]
    public async Task OpenTwoTransportBoxesWithDifferentCodes_ShouldSucceed()
    {
        // Arrange
        var firstBox = new TransportBox();
        await _repository.AddAsync(firstBox);
        await _repository.SaveChangesAsync();

        var secondBox = new TransportBox();
        await _repository.AddAsync(secondBox);
        await _repository.SaveChangesAsync();

        // Act - Otevřít s různými kódy
        var firstOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        var secondOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = secondBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B002"
        };

        var firstResult = await _handler.Handle(firstOpenRequest, CancellationToken.None);
        var secondResult = await _handler.Handle(secondOpenRequest, CancellationToken.None);

        // Assert - Oba by měly uspět
        firstResult.Success.Should().BeTrue();
        secondResult.Success.Should().BeTrue();

        var savedBoxes = await _repository.FindAsync(b => true);
        savedBoxes.Should().HaveCount(2);
        savedBoxes.Count(b => b.State == TransportBoxState.Opened).Should().Be(2);
    }

    [Fact]
    public async Task OpenTransportBoxWithCodeThenResetThenOpenAnotherWithSameCode_ShouldSucceed()
    {
        // Arrange - Vytvořit první box s kódem B001
        var firstBox = new TransportBox();
        await _repository.AddAsync(firstBox);
        await _repository.SaveChangesAsync();

        var firstOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        await _handler.Handle(firstOpenRequest, CancellationToken.None);

        // Reset první box (přejde do New stavu a kód se nastaví na null)
        var resetRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.New
        };

        await _handler.Handle(resetRequest, CancellationToken.None);

        // Act - Vytvořit druhý box se stejným kódem B001
        var secondBox = new TransportBox();
        await _repository.AddAsync(secondBox);
        await _repository.SaveChangesAsync();

        var secondOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = secondBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        var secondResult = await _handler.Handle(secondOpenRequest, CancellationToken.None);

        // Assert - Mělo by uspět, protože první box je teď v New stavu a nemá kód
        secondResult.Success.Should().BeTrue();

        var savedBoxes = await _repository.FindAsync(b => true);
        savedBoxes.Should().HaveCount(2);
        savedBoxes.Count(b => b.Code == "B001").Should().Be(1);
        savedBoxes.Count(b => b.Code == null).Should().Be(1);
    }

    [Fact]
    public async Task OpenTransportBoxWithCodeThenCloseItThenOpenAnotherWithSameCode_ShouldSucceed()
    {
        // Arrange - Vytvořit první box s kódem B001 a uzavřít ho
        var firstBox = new TransportBox();
        await _repository.AddAsync(firstBox);
        await _repository.SaveChangesAsync();

        // Otevřít první box
        var firstOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        await _handler.Handle(firstOpenRequest, CancellationToken.None);

        // Reset a uzavřít první box
        var resetRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.New
        };

        await _handler.Handle(resetRequest, CancellationToken.None);

        var closeRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = firstBox.Id,
            NewState = TransportBoxState.Closed
        };

        await _handler.Handle(closeRequest, CancellationToken.None);

        // Act - Pokusit se otevřít druhý box se stejným kódem B001
        var secondBox = new TransportBox();
        await _repository.AddAsync(secondBox);
        await _repository.SaveChangesAsync();

        var secondOpenRequest = new ChangeTransportBoxStateRequest
        {
            BoxId = secondBox.Id,
            NewState = TransportBoxState.Opened,
            BoxCode = "B001"
        };

        var secondResult = await _handler.Handle(secondOpenRequest, CancellationToken.None);

        // Assert - Mělo by uspět, protože uzavřený box není "aktivní"
        secondResult.Success.Should().BeTrue();
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }
}