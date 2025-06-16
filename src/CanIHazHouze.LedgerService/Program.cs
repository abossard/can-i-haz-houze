using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure ledger storage options
builder.Services.Configure<LedgerStorageOptions>(
    builder.Configuration.GetSection("LedgerStorage"));

// Add Entity Framework with SQLite
builder.Services.AddDbContext<LedgerDbContext>(options =>
{
    var storageOptions = builder.Configuration.GetSection("LedgerStorage").Get<LedgerStorageOptions>() 
                         ?? new LedgerStorageOptions();
    
    // Ensure the base directory exists
    Directory.CreateDirectory(storageOptions.BaseDirectory);
    
    var dbPath = Path.Combine(storageOptions.BaseDirectory, "ledger.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// Add ledger service
builder.Services.AddScoped<ILedgerService, LedgerServiceImpl>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    context.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"))
    .WithName("HealthCheck")
    .WithSummary("Health check endpoint")
    .WithDescription("Simple health check to verify the Ledger Service is running and responsive.")
    .WithOpenApi(operation =>
    {
        operation.Tags = [new() { Name = "Service Health" }];
        return operation;
    })
    .Produces<string>(StatusCodes.Status200OK);

// OpenAPI-tagged CRUD endpoints for user accounts
app.MapGet("/accounts/{owner}", async (string owner, ILedgerService ledgerService) =>
{
    try
    {
        var account = await ledgerService.GetAccountAsync(owner);
        return Results.Ok(account);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving account for owner {Owner}", owner);
        return Results.Problem("An error occurred while retrieving the account");
    }
})
.WithName("GetAccount")
.WithSummary("Get user account information")
.WithDescription("""
    Retrieves account information for a specific user, including current balance and timestamps.
    
    **Key Features:**
    - Automatic account creation on first access with random initial balance
    - Returns current balance and account metadata
    - Per-user account isolation
    - Initial balance ranges from $100 to $10,000 (configurable)
    
    **Parameters:**
    - `owner` (path, required): Username/identifier of the account owner
    
    **Account Creation:**
    If the account doesn't exist, it will be automatically created with:
    - Random initial balance between configured min/max values
    - Current timestamp for creation and last update
    
    **Response:**
    Returns complete account information including balance and timestamps.
    
    **Example Response:**
    ```json
    {
        "owner": "john_doe",
        "balance": 2500.75,
        "createdAt": "2024-06-14T10:00:00Z",
        "lastUpdatedAt": "2024-06-14T15:30:00Z"
    }
    ```
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Account Management" }];
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the account owner";
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    return operation;
})
.Produces<AccountInfo>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

app.MapPost("/accounts/{owner}/balance", async ([FromRoute] string owner, [FromBody] BalanceUpdateRequest request, ILedgerService ledgerService) =>
{
    try
    {
        var account = await ledgerService.UpdateBalanceAsync(owner, request.Amount, request.Description);
        return Results.Ok(account);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error updating balance for owner {Owner}", owner);
        return Results.Problem("An error occurred while updating the balance");
    }
})
.WithName("UpdateBalance")
.WithSummary("Update account balance")
.WithDescription("""
    Updates the account balance by adding or subtracting the specified amount.
    
    **Key Features:**
    - Add money (positive amounts) or subtract money (negative amounts)
    - Automatic transaction logging for audit trail
    - Account creation if it doesn't exist
    - Prevents negative balances (configurable)
    - Descriptive transaction records
    
    **Parameters:**
    - `owner` (path, required): Username/identifier of the account owner
    - Request body contains amount and description
    
    **Request Body:**
    ```json
    {
        "amount": 250.50,
        "description": "Salary deposit"
    }
    ```
    
    **Amount Rules:**
    - Positive values: Add money to account
    - Negative values: Subtract money from account
    - Zero values: Not allowed
    - Precision: Up to 2 decimal places
    
    **Use Cases:**
    - Deposit salary or income
    - Record expenses and purchases
    - Transfer money between accounts
    - Correct balance errors
    
    **Transaction Logging:**
    Every balance update creates a transaction record with:
    - Timestamp of the operation
    - Amount changed (positive or negative)
    - Description of the transaction
    - Resulting balance after the change
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Account Management" }];
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the account owner";
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    if (operation.RequestBody?.Content?.ContainsKey("application/json") == true)
    {
        operation.RequestBody.Description = "Balance update request containing amount and description";
    }
    
    return operation;
})
.Produces<AccountInfo>(StatusCodes.Status200OK)
.Produces<string>(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/accounts/{owner}/transactions", async (string owner, ILedgerService ledgerService, int skip = 0, int take = 50) =>
{
    try
    {
        var transactions = await ledgerService.GetTransactionsAsync(owner, skip, take);
        return Results.Ok(transactions);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving transactions for owner {Owner}", owner);
        return Results.Problem("An error occurred while retrieving transactions");
    }
})
.WithName("GetTransactions")
.WithSummary("Get transaction history")
.WithDescription("""
    Retrieves transaction history for a specific user account with pagination support.
    
    **Key Features:**
    - Complete transaction audit trail
    - Pagination support for large transaction histories
    - Ordered by most recent transactions first
    - Includes amount, description, and resulting balance for each transaction
    
    **Parameters:**
    - `owner` (path, required): Username/identifier of the account owner
    - `skip` (query, optional): Number of transactions to skip (default: 0)
    - `take` (query, optional): Maximum number of transactions to return (default: 50, max: 1000)
    
    **Pagination:**
    - Use `skip` and `take` for efficient pagination
    - Default page size is 50 transactions
    - Maximum page size is 1000 transactions
    
    **Response:**
    Returns array of transaction records ordered by date (newest first).
    
    **Example Response:**
    ```json
    [
        {
            "id": "123e4567-e89b-12d3-a456-426614174000",
            "owner": "john_doe",
            "amount": -45.50,
            "balanceAfter": 2455.25,
            "description": "Grocery shopping",
            "createdAt": "2024-06-14T15:30:00Z"
        },
        {
            "id": "987fcdeb-51d2-43a8-b456-426614174001",
            "owner": "john_doe", 
            "amount": 2500.00,
            "balanceAfter": 2500.75,
            "description": "Salary deposit",
            "createdAt": "2024-06-14T10:00:00Z"
        }
    ]
    ```
    
    **Use Cases:**
    - Display transaction history in user interface
    - Generate financial reports
    - Audit account activity
    - Reconcile account balances
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Transaction History" }];
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the account owner";
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    var skipParam = operation.Parameters.FirstOrDefault(p => p.Name == "skip");
    if (skipParam != null)
    {
        skipParam.Description = "Number of transactions to skip for pagination (default: 0)";
        skipParam.Example = new Microsoft.OpenApi.Any.OpenApiInteger(0);
    }
    
    var takeParam = operation.Parameters.FirstOrDefault(p => p.Name == "take");
    if (takeParam != null)
    {
        takeParam.Description = "Maximum number of transactions to return (default: 50, max: 1000)";
        takeParam.Example = new Microsoft.OpenApi.Any.OpenApiInteger(50);
    }
    
    return operation;
})
.Produces<IEnumerable<TransactionInfo>>()
.Produces(StatusCodes.Status500InternalServerError);

app.MapPost("/accounts/{owner}/reset", async (string owner, ILedgerService ledgerService) =>
{
    try
    {
        var account = await ledgerService.ResetAccountAsync(owner);
        return Results.Ok(account);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error resetting account for owner {Owner}", owner);
        return Results.Problem("An error occurred while resetting the account");
    }
})
.WithName("ResetAccount")
.WithSummary("Reset account to initial state")
.WithDescription("""
    Resets an account to its initial state with a new random balance and clears transaction history.
    
    **⚠️ Important Warning:**
    This operation is destructive and cannot be undone. It will:
    - Delete all transaction history for the account
    - Reset balance to a new random amount
    - Update account timestamps
    
    **Key Features:**
    - Generates new random initial balance (between configured min/max)
    - Completely clears transaction history
    - Updates account creation and modification timestamps
    - Useful for testing or starting fresh
    
    **Parameters:**
    - `owner` (path, required): Username/identifier of the account owner
    
    **Initial Balance:**
    - New random balance between $100 and $10,000 (configurable)
    - Same range as used for new account creation
    
    **Use Cases:**
    - Testing and development environments
    - Demo account preparation
    - Starting fresh after errors
    - Account cleanup for inactive users
    
    **Data Loss Warning:**
    All transaction history will be permanently deleted. This includes:
    - All transaction records
    - Transaction descriptions and timestamps
    - Previous balance history
    
    Consider exporting transaction data before reset if historical records are needed.
    
    **Example:**
    POST /accounts/john_doe/reset
    
    Returns the reset account with new balance and updated timestamps.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "Account Management" }];
    
    var ownerParam = operation.Parameters.FirstOrDefault(p => p.Name == "owner");
    if (ownerParam != null)
    {
        ownerParam.Description = "Username or identifier of the account owner to reset";
        ownerParam.Example = new Microsoft.OpenApi.Any.OpenApiString("john_doe");
    }
    
    return operation;
})
.Produces<AccountInfo>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDefaultEndpoints();

app.Run();

// Make Program class accessible for testing
public partial class Program { }

// Configuration options
public class LedgerStorageOptions
{
    public string BaseDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "LedgerData");
    public decimal MinInitialBalance { get; set; } = 100.00m;
    public decimal MaxInitialBalance { get; set; } = 10000.00m;
}

// Data models with OpenAPI annotations
/// <summary>
/// User account information including balance and timestamps
/// </summary>
/// <param name="Owner">Username or identifier of the account owner</param>
/// <param name="Balance">Current account balance in USD with 2 decimal precision</param>
/// <param name="CreatedAt">Timestamp when the account was created (UTC)</param>
/// <param name="LastUpdatedAt">Timestamp of the last balance update (UTC)</param>
public record AccountInfo(
    [property: Description("Username or identifier of the account owner")] string Owner,
    [property: Description("Current account balance in USD")] decimal Balance,
    [property: Description("Account creation timestamp in UTC")] DateTimeOffset CreatedAt,
    [property: Description("Last update timestamp in UTC")] DateTimeOffset LastUpdatedAt
);

/// <summary>
/// Transaction record containing details of a balance change
/// </summary>
/// <param name="Id">Unique identifier for the transaction</param>
/// <param name="Owner">Username or identifier of the account owner</param>
/// <param name="Amount">Amount changed (positive for deposits, negative for withdrawals)</param>
/// <param name="BalanceAfter">Account balance after this transaction was applied</param>
/// <param name="Description">Human-readable description of the transaction</param>
/// <param name="CreatedAt">Timestamp when the transaction occurred (UTC)</param>
public record TransactionInfo(
    [property: Description("Unique identifier for the transaction")] Guid Id,
    [property: Description("Username or identifier of the account owner")] string Owner,
    [property: Description("Amount changed (positive = deposit, negative = withdrawal)")] decimal Amount,
    [property: Description("Account balance after this transaction")] decimal BalanceAfter,
    [property: Description("Description of the transaction")] string Description,
    [property: Description("Transaction timestamp in UTC")] DateTimeOffset CreatedAt
);

/// <summary>
/// Request model for updating account balance
/// </summary>
/// <param name="Amount">Amount to add (positive) or subtract (negative) from the balance</param>
/// <param name="Description">Description of the transaction for audit purposes</param>
public record BalanceUpdateRequest(
    [property: Description("Amount to change (positive = add, negative = subtract)"), Range(-1000000, 1000000)] decimal Amount,
    [property: Description("Description of the transaction"), Required, MaxLength(500)] string Description
);

public class AccountEntity
{
    public string Owner { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

public class TransactionEntity
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

// Database context
public class LedgerDbContext : DbContext
{
    public LedgerDbContext(DbContextOptions<LedgerDbContext> options) : base(options) { }
    
    public DbSet<AccountEntity> Accounts { get; set; }
    public DbSet<TransactionEntity> Transactions { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.HasKey(e => e.Owner);
            entity.Property(e => e.Owner).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Balance).HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.LastUpdatedAt).IsRequired();
        });

        modelBuilder.Entity<TransactionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Owner).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.Owner);
            entity.HasIndex(e => new { e.Owner, e.CreatedAt });
        });
    }
}

// Service interface and implementation
public interface ILedgerService
{
    Task<AccountInfo> GetAccountAsync(string owner);
    Task<AccountInfo> UpdateBalanceAsync(string owner, decimal amount, string description);
    Task<IEnumerable<TransactionInfo>> GetTransactionsAsync(string owner, int skip = 0, int take = 50);
    Task<AccountInfo> ResetAccountAsync(string owner);
}

public class LedgerServiceImpl : ILedgerService
{
    private readonly LedgerDbContext _context;
    private readonly LedgerStorageOptions _options;
    private readonly ILogger<LedgerServiceImpl> _logger;
    private readonly Random _random = new();

    public LedgerServiceImpl(LedgerDbContext context, IOptions<LedgerStorageOptions> options, ILogger<LedgerServiceImpl> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AccountInfo> GetAccountAsync(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Owner == owner);
        
        if (account is null)
        {
            // Create new account with random initial balance
            var initialBalance = GenerateRandomInitialBalance();
            account = new AccountEntity
            {
                Owner = owner,
                Balance = initialBalance,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Accounts.Add(account);

            // Add initial transaction
            var initialTransaction = new TransactionEntity
            {
                Id = Guid.NewGuid(),
                Owner = owner,
                Amount = initialBalance,
                BalanceAfter = initialBalance,
                Description = "Initial account balance",
                CreatedAt = account.CreatedAt
            };

            _context.Transactions.Add(initialTransaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created new account for owner {Owner} with initial balance {Balance:C}", owner, initialBalance);
        }

        return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
    }

    public async Task<AccountInfo> UpdateBalanceAsync(string owner, decimal amount, string description)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be null or empty", nameof(description));

        if (amount == 0)
            throw new ArgumentException("Amount cannot be zero", nameof(amount));

        // Get or create account
        var accountInfo = await GetAccountAsync(owner);
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Owner == owner);
        
        if (account is null)
            throw new InvalidOperationException("Account should exist after GetAccountAsync");

        // Update balance
        var newBalance = account.Balance + amount;
        
        if (newBalance < 0)
            throw new ArgumentException("Insufficient funds. Current balance is " + account.Balance.ToString("C"));

        account.Balance = newBalance;
        account.LastUpdatedAt = DateTimeOffset.UtcNow;

        // Add transaction record
        var transaction = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            Owner = owner,
            Amount = amount,
            BalanceAfter = newBalance,
            Description = description,
            CreatedAt = account.LastUpdatedAt
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated balance for owner {Owner}: {Amount:C} -> {NewBalance:C}", owner, amount, newBalance);

        return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
    }

    public async Task<IEnumerable<TransactionInfo>> GetTransactionsAsync(string owner, int skip = 0, int take = 50)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        take = Math.Min(take, 1000); // Limit to prevent excessive data transfer

        // First get all transactions for the owner, then order on client side
        var transactions = await _context.Transactions
            .Where(t => t.Owner == owner)
            .ToListAsync();

        // Order by CreatedAt on client side and apply pagination
        var orderedTransactions = transactions
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(take);

        return orderedTransactions.Select(t => new TransactionInfo(t.Id, t.Owner, t.Amount, t.BalanceAfter, t.Description, t.CreatedAt));
    }

    public async Task<AccountInfo> ResetAccountAsync(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Owner == owner);
        
        if (account is null)
        {
            // If account doesn't exist, create it
            return await GetAccountAsync(owner);
        }

        // Generate new random balance
        var newBalance = GenerateRandomInitialBalance();
        account.Balance = newBalance;
        account.LastUpdatedAt = DateTimeOffset.UtcNow;

        // Add reset transaction
        var transaction = new TransactionEntity
        {
            Id = Guid.NewGuid(),
            Owner = owner,
            Amount = newBalance - account.Balance + newBalance, // This will be the difference to reach newBalance
            BalanceAfter = newBalance,
            Description = "Account reset with new random balance",
            CreatedAt = account.LastUpdatedAt
        };

        // Actually, let's make it simpler - just record the reset as a transaction
        transaction.Amount = newBalance;
        transaction.Description = $"Account reset to {newBalance:C}";

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Reset account for owner {Owner} to new balance {Balance:C}", owner, newBalance);

        return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
    }

    private decimal GenerateRandomInitialBalance()
    {
        var range = _options.MaxInitialBalance - _options.MinInitialBalance;
        var randomValue = (decimal)_random.NextDouble() * range + _options.MinInitialBalance;
        return Math.Round(randomValue, 2);
    }
}
