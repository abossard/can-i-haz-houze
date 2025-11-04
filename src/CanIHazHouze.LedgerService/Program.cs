// Temporary suppression of async no-await warnings; lambdas use awaited service calls but some helper methods intentionally synchronous
#pragma warning disable CS1998 // Async method lacks 'await'
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Net;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddMCPSupport();

// Add services to the container.
builder.Services.AddProblemDetails();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddOpenApiWithAzureContainerAppsServers();

// Configure ledger storage options
builder.Services.Configure<LedgerStorageOptions>(
    builder.Configuration.GetSection("LedgerStorage"));

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Add ledger service
builder.Services.AddScoped<ILedgerService, LedgerServiceImpl>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Use CORS
app.UseCors();

app.MapOpenApi();
app.MapScalarApiReference();

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

app.MapGet("/accounts/recent", async (ILedgerService ledgerService, int take = 10) =>
{
    try
    {
        var accounts = await ledgerService.GetRecentlyUpdatedAccountsAsync(take);
        return Results.Ok(accounts);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving recently updated accounts");
        return Results.Problem("An error occurred while retrieving recently updated accounts");
    }
})
.WithName("GetRecentlyUpdatedAccounts")
.WithSummary("Get recently updated user accounts")
.WithDescription("""
    Retrieves a list of user accounts ordered by their last update timestamp, showing the most recently active accounts first.
    
    **Key Features:**
    - System-wide view of account activity
    - Ordered by last update time (most recent first)
    - Shows account balance and metadata for each user
    - Useful for monitoring system activity and user engagement
    - Supports configurable result limit
    
    **Parameters:**
    - `take` (query, optional): Maximum number of accounts to return (default: 10, max: 100)
    
    **Use Cases:**
    - Dashboard showing recent user activity
    - Monitoring which users are actively using the system
    - Admin view of system engagement
    - Identifying most active users
    - Recent activity feed
    
    **Response:**
    Returns array of account information ordered by most recent activity.
    
    **Example Response:**
    ```json
    [
        {
            "owner": "jane_doe",
            "balance": 3456.78,
            "createdAt": "2024-06-10T08:00:00Z",
            "lastUpdatedAt": "2024-06-14T16:45:00Z"
        },
        {
            "owner": "john_smith",
            "balance": 1234.56,
            "createdAt": "2024-06-12T10:00:00Z",
            "lastUpdatedAt": "2024-06-14T15:30:00Z"
        },
        {
            "owner": "alice_wonder",
            "balance": 9876.54,
            "createdAt": "2024-06-11T09:00:00Z",
            "lastUpdatedAt": "2024-06-14T14:20:00Z"
        }
    ]
    ```
    
    **Ordering:**
    Results are ordered by `lastUpdatedAt` in descending order (newest first).
    
    **Activity Tracking:**
    The last update timestamp is modified when:
    - Balance is updated (deposit or withdrawal)
    - Account is reset
    
    Initial account creation also sets the last update timestamp.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "System Activity" }];
    
    var takeParam = operation.Parameters.FirstOrDefault(p => p.Name == "take");
    if (takeParam != null)
    {
        takeParam.Description = "Maximum number of accounts to return (default: 10, max: 100)";
        takeParam.Example = new Microsoft.OpenApi.Any.OpenApiInteger(10);
    }
    
    return operation;
})
.Produces<IEnumerable<AccountInfo>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

app.MapGet("/transactions/recent", async (ILedgerService ledgerService, int take = 20) =>
{
    try
    {
        var transactions = await ledgerService.GetRecentTransactionsAsync(take);
        return Results.Ok(transactions);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving recent transactions");
        return Results.Problem("An error occurred while retrieving recent transactions");
    }
})
.WithName("GetRecentTransactions")
.WithSummary("Get recent transactions across all users")
.WithDescription("""
    Retrieves recent transactions from all users in the system, ordered by timestamp (most recent first).
    
    **Key Features:**
    - System-wide view of all transaction activity
    - Includes transactions from all user accounts
    - Ordered by transaction time (most recent first)
    - Shows transaction details including amount, description, and resulting balance
    - Useful for monitoring overall system activity
    
    **Parameters:**
    - `take` (query, optional): Maximum number of transactions to return (default: 20, max: 100)
    
    **Use Cases:**
    - Dashboard showing recent system-wide activity
    - Activity feed across all users
    - Monitoring transaction patterns
    - Admin overview of ledger operations
    - Audit trail for recent changes
    
    **Response:**
    Returns array of transaction records ordered by most recent first.
    
    **Example Response:**
    ```json
    [
        {
            "id": "123e4567-e89b-12d3-a456-426614174000",
            "owner": "jane_doe",
            "amount": 250.00,
            "balanceAfter": 3456.78,
            "description": "Salary deposit",
            "createdAt": "2024-06-14T16:45:00Z"
        },
        {
            "id": "987fcdeb-51d2-43a8-b456-426614174001",
            "owner": "john_smith",
            "amount": -45.50,
            "balanceAfter": 1234.56,
            "description": "Grocery shopping",
            "createdAt": "2024-06-14T15:30:00Z"
        },
        {
            "id": "456789ab-cdef-1234-5678-90abcdef1234",
            "owner": "alice_wonder",
            "amount": 1000.00,
            "balanceAfter": 9876.54,
            "description": "Client payment",
            "createdAt": "2024-06-14T14:20:00Z"
        }
    ]
    ```
    
    **Transaction Types:**
    - Positive amounts indicate deposits/credits
    - Negative amounts indicate withdrawals/debits
    - Each transaction shows the resulting balance after the operation
    
    **Privacy Note:**
    This endpoint shows transaction data from all users. In production environments, 
    consider implementing appropriate access controls based on user roles and permissions.
    """)
.WithOpenApi(operation =>
{
    operation.Tags = [new() { Name = "System Activity" }];
    
    var takeParam = operation.Parameters.FirstOrDefault(p => p.Name == "take");
    if (takeParam != null)
    {
        takeParam.Description = "Maximum number of transactions to return (default: 20, max: 100)";
        takeParam.Example = new Microsoft.OpenApi.Any.OpenApiInteger(20);
    }
    
    return operation;
})
.Produces<IEnumerable<TransactionInfo>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

app.MapDefaultEndpoints();

// TODO: MCP tool registration needs migration to official SDK
// The official SDK requires using [McpServerToolType] and [McpServerTool] attributes
// or registering tools via builder.Services.AddMcpServer().WithTools<TToolsClass>()
// For now, tools are exposed via REST API endpoints above and can be called directly via HTTP
app.Logger.LogInformation("LedgerService REST API endpoints registered (MCP tools pending migration)");

app.Run();

#pragma warning restore CS1998

// Make Program class accessible for testing
namespace CanIHazHouze.LedgerService
{
    public partial class Program { }
}

// Configuration options
public class LedgerStorageOptions
{
    public string BaseDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "LedgerData");
    public decimal MinInitialBalance { get; set; } = 100.00m;
    public decimal MaxInitialBalance { get; set; } = 10000.00m;
    public int MaxQueryLimit { get; set; } = 100; // Maximum items to return in list queries
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
    public string id { get; set; } = string.Empty; // Cosmos DB id property  
    public string owner { get; set; } = string.Empty; // Partition key (username)
    public string Owner { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public string Type { get; set; } = "account"; // Document type discriminator
}

public class TransactionEntity
{
    public string id { get; set; } = string.Empty; // Cosmos DB id property
    public string owner { get; set; } = string.Empty; // Partition key (username)
    public Guid TransactionId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Type { get; set; } = "transaction"; // Document type discriminator
}

// Service interface and implementation
public interface ILedgerService
{
    Task<AccountInfo> GetAccountAsync(string owner);
    Task<AccountInfo> UpdateBalanceAsync(string owner, decimal amount, string description);
    Task<IEnumerable<TransactionInfo>> GetTransactionsAsync(string owner, int skip = 0, int take = 50);
    Task<AccountInfo> ResetAccountAsync(string owner);
    Task<IEnumerable<AccountInfo>> GetRecentlyUpdatedAccountsAsync(int take = 10);
    Task<IEnumerable<TransactionInfo>> GetRecentTransactionsAsync(int take = 20);
}

public class LedgerServiceImpl : ILedgerService
{
    private readonly CosmosClient _cosmosClient;
    private readonly LedgerStorageOptions _options;
    private readonly ILogger<LedgerServiceImpl> _logger;
    private readonly Random _random = new();
    private readonly Microsoft.Azure.Cosmos.Container _container;

    public LedgerServiceImpl(CosmosClient cosmosClient, IOptions<LedgerStorageOptions> options, ILogger<LedgerServiceImpl> logger)
    {
        _cosmosClient = cosmosClient;
        _options = options.Value;
        _logger = logger;
        _container = _cosmosClient.GetContainer("houze", "ledgers");
    }

    public async Task<AccountInfo> GetAccountAsync(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        try
        {
            var response = await _container.ReadItemAsync<AccountEntity>(
                id: $"account:{owner}",
                partitionKey: new PartitionKey(owner));
            
            var account = response.Resource;
            return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Create new account with random initial balance
            var initialBalance = GenerateRandomInitialBalance();
            var now = DateTimeOffset.UtcNow;
            
            var account = new AccountEntity
            {
                id = $"account:{owner}",
                owner = owner,
                Owner = owner,
                Balance = initialBalance,
                CreatedAt = now,
                LastUpdatedAt = now,
                Type = "account"
            };

            await _container.CreateItemAsync(account, new PartitionKey(owner));

            // Add initial transaction
            var initialTransaction = new TransactionEntity
            {
                id = $"transaction:{Guid.NewGuid()}",
                owner = owner,
                TransactionId = Guid.NewGuid(),
                Owner = owner,
                Amount = initialBalance,
                BalanceAfter = initialBalance,
                Description = "Initial account balance",
                CreatedAt = now,
                Type = "transaction"
            };

            await _container.CreateItemAsync(initialTransaction, new PartitionKey(owner));

            _logger.LogInformation("Created new account for owner {Owner} with initial balance {Balance:C}", owner, initialBalance);
            
            return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
        }
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
        
        try
        {
            var response = await _container.ReadItemAsync<AccountEntity>(
                id: $"account:{owner}",
                partitionKey: new PartitionKey(owner));
            
            var account = response.Resource;
            
            // Update balance
            var newBalance = account.Balance + amount;
            
            if (newBalance < 0)
                throw new ArgumentException("Insufficient funds. Current balance is " + account.Balance.ToString("C"));

            account.Balance = newBalance;
            account.LastUpdatedAt = DateTimeOffset.UtcNow;

            // Update account
            await _container.ReplaceItemAsync(account, account.id, new PartitionKey(owner));

            // Add transaction record
            var transaction = new TransactionEntity
            {
                id = $"transaction:{Guid.NewGuid()}",
                owner = owner,
                TransactionId = Guid.NewGuid(),
                Owner = owner,
                Amount = amount,
                BalanceAfter = newBalance,
                Description = description,
                CreatedAt = account.LastUpdatedAt,
                Type = "transaction"
            };

            await _container.CreateItemAsync(transaction, new PartitionKey(owner));

            _logger.LogInformation("Updated balance for owner {Owner}: {Amount:C} -> {NewBalance:C}", owner, amount, newBalance);

            return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error updating balance for owner {Owner}", owner);
            throw;
        }
    }

    public async Task<IEnumerable<TransactionInfo>> GetTransactionsAsync(string owner, int skip = 0, int take = 50)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        take = Math.Min(take, 1000); // Limit to prevent excessive data transfer

        try
        {
            // Select only needed fields to reduce RU consumption
            var query = new QueryDefinition(
                "SELECT c.TransactionId, c.Owner, c.Amount, c.BalanceAfter, c.Description, c.CreatedAt FROM c WHERE c.owner = @owner AND c.Type = @type ORDER BY c.CreatedAt DESC OFFSET @skip LIMIT @take")
                .WithParameter("@owner", owner)
                .WithParameter("@type", "transaction")
                .WithParameter("@skip", skip)
                .WithParameter("@take", take);

            var iterator = _container.GetItemQueryIterator<TransactionEntity>(query);
            var transactions = new List<TransactionEntity>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                transactions.AddRange(response);
            }

            return transactions.Select(t => new TransactionInfo(
                t.TransactionId, 
                t.Owner, 
                t.Amount, 
                t.BalanceAfter, 
                t.Description, 
                t.CreatedAt));
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving transactions for owner {Owner}", owner);
            throw;
        }
    }

    public async Task<AccountInfo> ResetAccountAsync(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be null or empty", nameof(owner));

        try
        {
            var response = await _container.ReadItemAsync<AccountEntity>(
                id: $"account:{owner}",
                partitionKey: new PartitionKey(owner));
            
            var account = response.Resource;
            
            // Generate new random balance
            var newBalance = GenerateRandomInitialBalance();
            account.Balance = newBalance;
            account.LastUpdatedAt = DateTimeOffset.UtcNow;

            // Update account
            await _container.ReplaceItemAsync(account, account.id, new PartitionKey(owner));

            // Add reset transaction
            var transaction = new TransactionEntity
            {
                id = $"transaction:{Guid.NewGuid()}",
                owner = owner,
                TransactionId = Guid.NewGuid(),
                Owner = owner,
                Amount = newBalance,
                BalanceAfter = newBalance,
                Description = $"Account reset to {newBalance:C}",
                CreatedAt = account.LastUpdatedAt,
                Type = "transaction"
            };

            await _container.CreateItemAsync(transaction, new PartitionKey(owner));

            _logger.LogInformation("Reset account for owner {Owner} to new balance {Balance:C}", owner, newBalance);

            return new AccountInfo(account.Owner, account.Balance, account.CreatedAt, account.LastUpdatedAt);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // If account doesn't exist, create it
            return await GetAccountAsync(owner);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error resetting account for owner {Owner}", owner);
            throw;
        }
    }

    private decimal GenerateRandomInitialBalance()
    {
        var range = _options.MaxInitialBalance - _options.MinInitialBalance;
        var randomValue = (decimal)_random.NextDouble() * range + _options.MinInitialBalance;
        return Math.Round(randomValue, 2);
    }

    public async Task<IEnumerable<AccountInfo>> GetRecentlyUpdatedAccountsAsync(int take = 10)
    {
        take = Math.Min(take, _options.MaxQueryLimit); // Limit to prevent excessive data transfer

        try
        {
            // Query for accounts ordered by LastUpdatedAt descending, selecting only needed fields
            var query = new QueryDefinition(
                "SELECT c.Owner, c.Balance, c.CreatedAt, c.LastUpdatedAt FROM c WHERE c.Type = @type ORDER BY c.LastUpdatedAt DESC OFFSET 0 LIMIT @take")
                .WithParameter("@type", "account")
                .WithParameter("@take", take);

            var iterator = _container.GetItemQueryIterator<AccountEntity>(query);
            var accounts = new List<AccountEntity>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                accounts.AddRange(response);
            }

            return accounts.Select(a => new AccountInfo(
                a.Owner,
                a.Balance,
                a.CreatedAt,
                a.LastUpdatedAt));
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving recently updated accounts");
            throw;
        }
    }

    public async Task<IEnumerable<TransactionInfo>> GetRecentTransactionsAsync(int take = 20)
    {
        take = Math.Min(take, _options.MaxQueryLimit); // Limit to prevent excessive data transfer

        try
        {
            // Query for transactions ordered by CreatedAt descending, selecting only needed fields
            var query = new QueryDefinition(
                "SELECT c.TransactionId, c.Owner, c.Amount, c.BalanceAfter, c.Description, c.CreatedAt FROM c WHERE c.Type = @type ORDER BY c.CreatedAt DESC OFFSET 0 LIMIT @take")
                .WithParameter("@type", "transaction")
                .WithParameter("@take", take);

            var iterator = _container.GetItemQueryIterator<TransactionEntity>(query);
            var transactions = new List<TransactionEntity>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                transactions.AddRange(response);
            }

            return transactions.Select(t => new TransactionInfo(
                t.TransactionId,
                t.Owner,
                t.Amount,
                t.BalanceAfter,
                t.Description,
                t.CreatedAt));
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving recent transactions");
            throw;
        }
    }
}

// MCP Tool Request Models
/// <summary>
/// Request model for getting account information via MCP
/// </summary>
/// <param name="Owner">Username or identifier of the account owner</param>
public record GetAccountRequest(string Owner);

/// <summary>
/// Request model for updating account balance via MCP
/// </summary>
/// <param name="Owner">Username or identifier of the account owner</param>
/// <param name="Amount">Amount to add (positive) or subtract (negative) from the balance</param>
/// <param name="Description">Description of the transaction for audit purposes</param>
public record UpdateBalanceRequest(string Owner, decimal Amount, string Description);

/// <summary>
/// Request model for getting transaction history via MCP
/// </summary>
/// <param name="Owner">Username or identifier of the account owner</param>
/// <param name="Skip">Number of transactions to skip for pagination (default: 0)</param>
/// <param name="Take">Maximum number of transactions to return (default: 50)</param>
public record GetTransactionsRequest(string Owner, int Skip = 0, int Take = 50);

/// <summary>
/// Request model for resetting account via MCP
/// </summary>
/// <param name="Owner">Username or identifier of the account owner</param>
public record ResetAccountRequest(string Owner);
