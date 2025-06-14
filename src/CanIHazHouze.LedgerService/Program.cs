using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
}

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"))
    .WithName("HealthCheck");

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

// Data models
public record AccountInfo(string Owner, decimal Balance, DateTimeOffset CreatedAt, DateTimeOffset LastUpdatedAt);
public record TransactionInfo(Guid Id, string Owner, decimal Amount, decimal BalanceAfter, string Description, DateTimeOffset CreatedAt);
public record BalanceUpdateRequest(decimal Amount, string Description);

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

        var transactions = await _context.Transactions
            .Where(t => t.Owner == owner)
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return transactions.Select(t => new TransactionInfo(t.Id, t.Owner, t.Amount, t.BalanceAfter, t.Description, t.CreatedAt));
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
