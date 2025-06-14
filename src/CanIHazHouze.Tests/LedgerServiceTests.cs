using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CanIHazHouze.Tests;

public class LedgerServiceTests
{
    private LedgerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new LedgerDbContext(options);
    }

    private LedgerServiceImpl CreateService(LedgerDbContext context, LedgerStorageOptions? options = null)
    {
        options ??= new LedgerStorageOptions();
        var optionsWrapper = Options.Create(options);
        var logger = NullLogger<LedgerServiceImpl>.Instance;
        return new LedgerServiceImpl(context, optionsWrapper, logger);
    }

    [Fact]
    public async Task GetAccount_CreatesNewAccountWithRandomBalance_WhenAccountDoesNotExist()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";

        // Act
        var account = await service.GetAccountAsync(owner);

        // Assert
        Assert.Equal(owner, account.Owner);
        Assert.True(account.Balance >= 100m && account.Balance <= 10000m); // Default range
        Assert.True(account.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.Equal(account.CreatedAt, account.LastUpdatedAt);

        // Verify account is stored in database
        var storedAccount = await context.Accounts.FirstOrDefaultAsync(a => a.Owner == owner);
        Assert.NotNull(storedAccount);
        Assert.Equal(account.Balance, storedAccount.Balance);

        // Verify initial transaction is created
        var transactions = await context.Transactions.Where(t => t.Owner == owner).ToListAsync();
        Assert.Single(transactions);
        Assert.Equal(account.Balance, transactions[0].Amount);
        Assert.Equal("Initial account balance", transactions[0].Description);
    }

    [Fact]
    public async Task GetAccount_ReturnsExistingAccount_WhenAccountExists()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";

        // Create account first
        var firstCall = await service.GetAccountAsync(owner);

        // Act
        var secondCall = await service.GetAccountAsync(owner);

        // Assert
        Assert.Equal(firstCall.Owner, secondCall.Owner);
        Assert.Equal(firstCall.Balance, secondCall.Balance);
        Assert.Equal(firstCall.CreatedAt, secondCall.CreatedAt);
        Assert.Equal(firstCall.LastUpdatedAt, secondCall.LastUpdatedAt);
    }

    [Fact]
    public async Task UpdateBalance_AddsPositiveAmount_UpdatesBalanceCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";
        var initialAccount = await service.GetAccountAsync(owner);
        var addAmount = 500m;

        // Act
        var updatedAccount = await service.UpdateBalanceAsync(owner, addAmount, "Test deposit");

        // Assert
        Assert.Equal(initialAccount.Balance + addAmount, updatedAccount.Balance);
        Assert.True(updatedAccount.LastUpdatedAt > initialAccount.LastUpdatedAt);

        // Verify transaction is recorded
        var transactions = await context.Transactions
            .Where(t => t.Owner == owner && t.Description == "Test deposit")
            .ToListAsync();
        Assert.Single(transactions);
        Assert.Equal(addAmount, transactions[0].Amount);
        Assert.Equal(updatedAccount.Balance, transactions[0].BalanceAfter);
    }

    [Fact]
    public async Task UpdateBalance_SubtractsAmount_UpdatesBalanceCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";
        var initialAccount = await service.GetAccountAsync(owner);
        var withdrawAmount = -100m;

        // Act
        var updatedAccount = await service.UpdateBalanceAsync(owner, withdrawAmount, "Test withdrawal");

        // Assert
        Assert.Equal(initialAccount.Balance + withdrawAmount, updatedAccount.Balance);
        Assert.True(updatedAccount.LastUpdatedAt > initialAccount.LastUpdatedAt);
    }

    [Fact]
    public async Task UpdateBalance_ThrowsException_WhenInsufficientFunds()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var options = new LedgerStorageOptions { MinInitialBalance = 100m, MaxInitialBalance = 200m };
        var service = CreateService(context, options);
        var owner = "testuser";
        var account = await service.GetAccountAsync(owner);
        var largeWithdrawal = -(account.Balance + 1);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateBalanceAsync(owner, largeWithdrawal, "Large withdrawal"));
        
        Assert.Contains("Insufficient funds", exception.Message);
    }

    [Fact]
    public async Task UpdateBalance_ThrowsException_WhenAmountIsZero()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateBalanceAsync(owner, 0m, "Zero amount"));
        
        Assert.Contains("Amount cannot be zero", exception.Message);
    }

    [Fact]
    public async Task GetTransactions_ReturnsTransactionsInDescendingOrder()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";
        
        // Create account and make several transactions
        await service.GetAccountAsync(owner);
        await service.UpdateBalanceAsync(owner, 100m, "First transaction");
        await service.UpdateBalanceAsync(owner, 200m, "Second transaction");
        await service.UpdateBalanceAsync(owner, -50m, "Third transaction");

        // Act
        var transactions = await service.GetTransactionsAsync(owner);

        // Assert
        var transactionList = transactions.ToList();
        Assert.Equal(4, transactionList.Count); // Including initial account creation
        
        // Verify they are in descending order by creation time
        for (int i = 0; i < transactionList.Count - 1; i++)
        {
            Assert.True(transactionList[i].CreatedAt >= transactionList[i + 1].CreatedAt);
        }

        // Verify the descriptions are in reverse order
        Assert.Equal("Third transaction", transactionList[0].Description);
        Assert.Equal("Second transaction", transactionList[1].Description);
        Assert.Equal("First transaction", transactionList[2].Description);
        Assert.Equal("Initial account balance", transactionList[3].Description);
    }

    [Fact]
    public async Task ResetAccount_GeneratesNewRandomBalance()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var service = CreateService(context);
        var owner = "testuser";
        var originalAccount = await service.GetAccountAsync(owner);

        // Act
        var resetAccount = await service.ResetAccountAsync(owner);

        // Assert
        Assert.Equal(owner, resetAccount.Owner);
        Assert.True(resetAccount.Balance >= 100m && resetAccount.Balance <= 10000m);
        Assert.True(resetAccount.LastUpdatedAt > originalAccount.LastUpdatedAt);
        
        // In most cases, the balance should be different (very small chance they're the same)
        // We'll just verify the reset transaction was created
        var transactions = await context.Transactions
            .Where(t => t.Owner == owner && t.Description.Contains("Account reset"))
            .ToListAsync();
        Assert.Single(transactions);
    }

    [Fact]
    public void LedgerStorageOptions_HasCorrectDefaults()
    {
        // Arrange & Act
        var options = new LedgerStorageOptions();

        // Assert
        Assert.Contains("LedgerData", options.BaseDirectory);
        Assert.Equal(100.00m, options.MinInitialBalance);
        Assert.Equal(10000.00m, options.MaxInitialBalance);
    }

    [Fact]
    public void AccountInfo_RecordPropertiesWork()
    {
        // Arrange
        var owner = "testuser";
        var balance = 1234.56m;
        var createdAt = DateTimeOffset.UtcNow.AddDays(-1);
        var lastUpdatedAt = DateTimeOffset.UtcNow;

        // Act
        var accountInfo = new AccountInfo(owner, balance, createdAt, lastUpdatedAt);

        // Assert
        Assert.Equal(owner, accountInfo.Owner);
        Assert.Equal(balance, accountInfo.Balance);
        Assert.Equal(createdAt, accountInfo.CreatedAt);
        Assert.Equal(lastUpdatedAt, accountInfo.LastUpdatedAt);
    }

    [Fact]
    public void TransactionInfo_RecordPropertiesWork()
    {
        // Arrange
        var id = Guid.NewGuid();
        var owner = "testuser";
        var amount = 123.45m;
        var balanceAfter = 678.90m;
        var description = "Test transaction";
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var transactionInfo = new TransactionInfo(id, owner, amount, balanceAfter, description, createdAt);

        // Assert
        Assert.Equal(id, transactionInfo.Id);
        Assert.Equal(owner, transactionInfo.Owner);
        Assert.Equal(amount, transactionInfo.Amount);
        Assert.Equal(balanceAfter, transactionInfo.BalanceAfter);
        Assert.Equal(description, transactionInfo.Description);
        Assert.Equal(createdAt, transactionInfo.CreatedAt);
    }
}
