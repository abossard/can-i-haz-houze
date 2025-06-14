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

// Configure mortgage storage options
builder.Services.Configure<MortgageStorageOptions>(
    builder.Configuration.GetSection("MortgageStorage"));

// Add Entity Framework with SQLite
builder.Services.AddDbContext<MortgageDbContext>(options =>
{
    var storageOptions = builder.Configuration.GetSection("MortgageStorage").Get<MortgageStorageOptions>() 
                         ?? new MortgageStorageOptions();
    
    // Ensure the base directory exists
    Directory.CreateDirectory(storageOptions.BaseDirectory);
    
    var dbPath = Path.Combine(storageOptions.BaseDirectory, "mortgage.db");
    options.UseSqlite($"Data Source={dbPath}");
});

// Add mortgage approval service
builder.Services.AddScoped<IMortgageApprovalService, MortgageApprovalServiceImpl>();

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();
    context.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"));

// Mortgage Request API endpoints
app.MapPost("/mortgage-requests", async (
    [FromBody, Required] CreateMortgageRequestDto request,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var mortgageRequest = await mortgageService.CreateMortgageRequestAsync(request.UserName);
        return Results.Created($"/mortgage-requests/{mortgageRequest.Id}", mortgageRequest);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(ex.Message);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating mortgage request for user {UserName}", request.UserName);
        return Results.Problem("An error occurred while creating the mortgage request.");
    }
})
.WithName("CreateMortgageRequest")
.WithOpenApi()
.WithTags("MortgageRequests");

app.MapGet("/mortgage-requests/{requestId:guid}", async (
    Guid requestId,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var mortgageRequest = await mortgageService.GetMortgageRequestAsync(requestId);
        return mortgageRequest != null ? Results.Ok(mortgageRequest) : Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving mortgage request {RequestId}", requestId);
        return Results.Problem("An error occurred while retrieving the mortgage request.");
    }
})
.WithName("GetMortgageRequest")
.WithOpenApi()
.WithTags("MortgageRequests");

app.MapGet("/mortgage-requests/user/{userName}", async (
    string userName,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var mortgageRequest = await mortgageService.GetMortgageRequestByUserAsync(userName);
        return mortgageRequest != null ? Results.Ok(mortgageRequest) : Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving mortgage request for user {UserName}", userName);
        return Results.Problem("An error occurred while retrieving the mortgage request.");
    }
})
.WithName("GetMortgageRequestByUser")
.WithOpenApi()
.WithTags("MortgageRequests");

app.MapPut("/mortgage-requests/{requestId:guid}/data", async (
    Guid requestId,
    [FromBody, Required] UpdateMortgageDataDto updateData,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var mortgageRequest = await mortgageService.UpdateMortgageDataAsync(requestId, updateData.Data);
        return mortgageRequest != null ? Results.Ok(mortgageRequest) : Results.NotFound();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error updating mortgage request {RequestId}", requestId);
        return Results.Problem("An error occurred while updating the mortgage request.");
    }
})
.WithName("UpdateMortgageRequestData")
.WithOpenApi()
.WithTags("MortgageRequests");

app.MapDelete("/mortgage-requests/{requestId:guid}", async (
    Guid requestId,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var success = await mortgageService.DeleteMortgageRequestAsync(requestId);
        return success ? Results.NoContent() : Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error deleting mortgage request {RequestId}", requestId);
        return Results.Problem("An error occurred while deleting the mortgage request.");
    }
})
.WithName("DeleteMortgageRequest")
.WithOpenApi()
.WithTags("MortgageRequests");

app.MapGet("/mortgage-requests", async (
    IMortgageApprovalService mortgageService,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] string? status = null) =>
{
    try
    {
        var requests = await mortgageService.GetMortgageRequestsAsync(page, pageSize, status);
        return Results.Ok(requests);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving mortgage requests");
        return Results.Problem("An error occurred while retrieving mortgage requests.");
    }
})
.WithName("GetMortgageRequests")
.WithOpenApi()
.WithTags("MortgageRequests");

app.Run();

// DTOs for API endpoints
public record CreateMortgageRequestDto(string UserName);
public record UpdateMortgageDataDto(Dictionary<string, object> Data);

// Configuration options
public class MortgageStorageOptions
{
    public string BaseDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "MortgageData_Dev");
}

// Domain Models
public class MortgageRequest
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public MortgageRequestStatus Status { get; set; } = MortgageRequestStatus.Pending;
    public string StatusReason { get; set; } = "Application submitted - awaiting documentation";
    public string MissingRequirements { get; set; } = "All required documentation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string RequestDataJson { get; set; } = "{}"; // Store additional data as JSON
    
    // Navigation property for additional data
    public Dictionary<string, object> RequestData
    {
        get => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(RequestDataJson) ?? new Dictionary<string, object>();
        set => RequestDataJson = System.Text.Json.JsonSerializer.Serialize(value);
    }
}

public enum MortgageRequestStatus
{
    Pending,
    UnderReview,
    Approved,
    Rejected,
    RequiresAdditionalInfo
}

// Database Context
public class MortgageDbContext : DbContext
{
    public MortgageDbContext(DbContextOptions<MortgageDbContext> options) : base(options) { }

    public DbSet<MortgageRequest> MortgageRequests { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MortgageRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserName).IsUnique(); // Ensure one request per user
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StatusReason).HasMaxLength(500);
            entity.Property(e => e.MissingRequirements).HasMaxLength(1000);
            entity.Property(e => e.RequestDataJson).HasColumnType("TEXT");
            entity.Ignore(e => e.RequestData); // Don't map the computed property
        });
    }
}

// Service Interface
public interface IMortgageApprovalService
{
    Task<MortgageRequest> CreateMortgageRequestAsync(string userName);
    Task<MortgageRequest?> GetMortgageRequestAsync(Guid requestId);
    Task<MortgageRequest?> GetMortgageRequestByUserAsync(string userName);
    Task<MortgageRequest?> UpdateMortgageDataAsync(Guid requestId, Dictionary<string, object> newData);
    Task<bool> DeleteMortgageRequestAsync(Guid requestId);
    Task<IEnumerable<MortgageRequest>> GetMortgageRequestsAsync(int page, int pageSize, string? status);
}

// Service Implementation
public class MortgageApprovalServiceImpl : IMortgageApprovalService
{
    private readonly MortgageDbContext _context;
    private readonly ILogger<MortgageApprovalServiceImpl> _logger;

    public MortgageApprovalServiceImpl(MortgageDbContext context, ILogger<MortgageApprovalServiceImpl> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<MortgageRequest> CreateMortgageRequestAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Username cannot be empty", nameof(userName));

        // Check if user already has a request
        var existingRequest = await _context.MortgageRequests
            .FirstOrDefaultAsync(r => r.UserName == userName);

        if (existingRequest != null)
            throw new InvalidOperationException($"User {userName} already has an existing mortgage request");

        var mortgageRequest = new MortgageRequest
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Status = MortgageRequestStatus.Pending,
            StatusReason = "Application submitted - awaiting documentation",
            MissingRequirements = "Income verification, Credit report, Property appraisal, Employment verification",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.MortgageRequests.Add(mortgageRequest);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created mortgage request {RequestId} for user {UserName}", mortgageRequest.Id, userName);
        return mortgageRequest;
    }

    public async Task<MortgageRequest?> GetMortgageRequestAsync(Guid requestId)
    {
        return await _context.MortgageRequests.FindAsync(requestId);
    }

    public async Task<MortgageRequest?> GetMortgageRequestByUserAsync(string userName)
    {
        return await _context.MortgageRequests
            .FirstOrDefaultAsync(r => r.UserName == userName);
    }

    public async Task<MortgageRequest?> UpdateMortgageDataAsync(Guid requestId, Dictionary<string, object> newData)
    {
        var mortgageRequest = await _context.MortgageRequests.FindAsync(requestId);
        if (mortgageRequest == null) return null;

        // Merge new data with existing data
        var currentData = mortgageRequest.RequestData;
        foreach (var kvp in newData)
        {
            currentData[kvp.Key] = kvp.Value;
        }
        mortgageRequest.RequestData = currentData;
        mortgageRequest.UpdatedAt = DateTime.UtcNow;

        // Evaluate status based on the updated data
        EvaluateRequestStatus(mortgageRequest);

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated mortgage request {RequestId} with new data. Status: {Status}", 
            requestId, mortgageRequest.Status);
        
        return mortgageRequest;
    }

    public async Task<bool> DeleteMortgageRequestAsync(Guid requestId)
    {
        var mortgageRequest = await _context.MortgageRequests.FindAsync(requestId);
        if (mortgageRequest == null) return false;

        _context.MortgageRequests.Remove(mortgageRequest);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted mortgage request {RequestId}", requestId);
        return true;
    }

    public async Task<IEnumerable<MortgageRequest>> GetMortgageRequestsAsync(int page, int pageSize, string? status)
    {
        var query = _context.MortgageRequests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MortgageRequestStatus>(status, true, out var statusEnum))
        {
            query = query.Where(r => r.Status == statusEnum);
        }

        return await query
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    private void EvaluateRequestStatus(MortgageRequest request)
    {
        var data = request.RequestData;
        var missingRequirements = new List<string>();

        // Basic evaluation logic - this will be expanded later when you add specific data fields
        if (!data.ContainsKey("income_verification"))
            missingRequirements.Add("Income verification");
        
        if (!data.ContainsKey("credit_report"))
            missingRequirements.Add("Credit report");
        
        if (!data.ContainsKey("property_appraisal"))
            missingRequirements.Add("Property appraisal");
        
        if (!data.ContainsKey("employment_verification"))
            missingRequirements.Add("Employment verification");

        if (missingRequirements.Count == 0)
        {
            // All basic requirements met - perform approval logic
            var income = GetValueAsDecimal(data, "annual_income");
            var creditScore = GetValueAsInt(data, "credit_score");
            var loanAmount = GetValueAsDecimal(data, "loan_amount");

            if (income > 0 && creditScore > 0 && loanAmount > 0)
            {
                var debtToIncomeRatio = loanAmount / (income * 12); // Simple calculation
                
                if (creditScore >= 650 && debtToIncomeRatio <= 0.43m)
                {
                    request.Status = MortgageRequestStatus.Approved;
                    request.StatusReason = "Application approved - all criteria met";
                    request.MissingRequirements = string.Empty;
                }
                else
                {
                    request.Status = MortgageRequestStatus.Rejected;
                    var reasons = new List<string>();
                    if (creditScore < 650) reasons.Add($"Credit score too low ({creditScore} < 650)");
                    if (debtToIncomeRatio > 0.43m) reasons.Add($"Debt-to-income ratio too high ({debtToIncomeRatio:P2} > 43%)");
                    request.StatusReason = $"Application rejected: {string.Join(", ", reasons)}";
                    request.MissingRequirements = string.Empty;
                }
            }
            else
            {
                request.Status = MortgageRequestStatus.UnderReview;
                request.StatusReason = "All documents received - under manual review";
                request.MissingRequirements = string.Empty;
            }
        }
        else
        {
            request.Status = MortgageRequestStatus.RequiresAdditionalInfo;
            request.StatusReason = "Additional information required";
            request.MissingRequirements = string.Join(", ", missingRequirements);
        }
    }

    private decimal GetValueAsDecimal(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (decimal.TryParse(value?.ToString(), out var result))
                return result;
        }
        return 0;
    }

    private int GetValueAsInt(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (int.TryParse(value?.ToString(), out var result))
                return result;
        }
        return 0;
    }
}
