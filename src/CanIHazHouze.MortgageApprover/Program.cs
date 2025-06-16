using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/*
 * CanIHazHouze Mortgage Approver Service
 * 
 * This service manages mortgage applications with structured data collection and automated approval logic.
 * 
 * Data Structure Overview:
 * ======================
 * 
 * Mortgage requests store additional data in a flexible JSON structure (RequestData property).
 * The following data fields are recognized and used in the approval evaluation:
 * 
 * Income Verification Fields:
 * - income_annual: Annual income in dollars (decimal)
 * - income_employment_type: Employment type (string: full-time, part-time, contract, self-employed)
 * - income_years_employed: Years of employment (decimal, supports partial years)
 * 
 * Credit Report Fields:
 * - credit_score: Credit score (int, range 300-850)
 * - credit_report_date: Date of credit report (string, ISO format)
 * - credit_outstanding_debts: Outstanding debts in dollars (decimal)
 * 
 * Employment Verification Fields:
 * - employment_employer: Name of current employer (string)
 * - employment_job_title: Current job title (string)
 * - employment_monthly_salary: Monthly salary in dollars (decimal)
 * - employment_verified: Whether employment has been verified (bool)
 * 
 * Property Appraisal Fields:
 * - property_value: Appraised property value in dollars (decimal)
 * - property_loan_amount: Requested loan amount in dollars (decimal)
 * - property_type: Type of property (string: single-family, condo, townhouse, multi-family)
 * - property_appraisal_date: Date of property appraisal (string, ISO format)
 * - property_appraisal_completed: Whether appraisal has been completed (bool)
 * 
 * Approval Logic:
 * ==============
 * 
 * 1. All four requirement categories must have data present
 * 2. Credit score must be >= 650
 * 3. Debt-to-income ratio (monthly payment / monthly income) must be <= 43%
 * 4. Monthly payment is calculated using standard 30-year mortgage at 7% interest
 * 
 * Status Flow:
 * ===========
 * 
 * Pending -> RequiresAdditionalInfo -> UnderReview -> Approved/Rejected
 * 
 * - Pending: Initial state when request is created
 * - RequiresAdditionalInfo: Missing one or more requirement categories
 * - UnderReview: All requirements present but insufficient financial data for auto-approval
 * - Approved: Meets all automated approval criteria
 * - Rejected: Fails automated approval criteria (low credit score or high DTI ratio)
 */

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure JSON serialization to use string values for enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

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

/// <summary>
/// Typed data models for mortgage requirement fields
/// These models represent the structured data that can be stored in a mortgage request
/// </summary>
public static class MortgageDataFields
{
    /// <summary>
    /// Income verification related data fields
    /// </summary>
    public static class Income
    {
        /// <summary>Key: income_annual - Annual income in dollars (decimal)</summary>
        public const string Annual = "income_annual";
        
        /// <summary>Key: income_employment_type - Type of employment (string: full-time, part-time, contract, self-employed)</summary>
        public const string EmploymentType = "income_employment_type";
        
        /// <summary>Key: income_years_employed - Years of employment (decimal, supports half years)</summary>
        public const string YearsEmployed = "income_years_employed";
    }

    /// <summary>
    /// Credit report related data fields
    /// </summary>
    public static class Credit
    {
        /// <summary>Key: credit_score - Credit score (int, range 300-850)</summary>
        public const string Score = "credit_score";
        
        /// <summary>Key: credit_report_date - Date of credit report (string, ISO date format)</summary>
        public const string ReportDate = "credit_report_date";
        
        /// <summary>Key: credit_outstanding_debts - Outstanding debts in dollars (decimal)</summary>
        public const string OutstandingDebts = "credit_outstanding_debts";
    }

    /// <summary>
    /// Employment verification related data fields
    /// </summary>
    public static class Employment
    {
        /// <summary>Key: employment_employer - Name of current employer (string)</summary>
        public const string Employer = "employment_employer";
        
        /// <summary>Key: employment_job_title - Current job title (string)</summary>
        public const string JobTitle = "employment_job_title";
        
        /// <summary>Key: employment_monthly_salary - Monthly salary in dollars (decimal)</summary>
        public const string MonthlySalary = "employment_monthly_salary";
        
        /// <summary>Key: employment_verified - Whether employment has been verified (bool)</summary>
        public const string Verified = "employment_verified";
    }

    /// <summary>
    /// Property appraisal related data fields
    /// </summary>
    public static class Property
    {
        /// <summary>Key: property_value - Appraised property value in dollars (decimal)</summary>
        public const string Value = "property_value";
        
        /// <summary>Key: property_loan_amount - Requested loan amount in dollars (decimal)</summary>
        public const string LoanAmount = "property_loan_amount";
        
        /// <summary>Key: property_type - Type of property (string: single-family, condo, townhouse, multi-family)</summary>
        public const string Type = "property_type";
        
        /// <summary>Key: property_appraisal_date - Date of property appraisal (string, ISO date format)</summary>
        public const string AppraisalDate = "property_appraisal_date";
        
        /// <summary>Key: property_appraisal_completed - Whether appraisal has been completed (bool)</summary>
        public const string AppraisalCompleted = "property_appraisal_completed";
    }

    /// <summary>
    /// Legacy field keys for backward compatibility
    /// </summary>
    public static class Legacy
    {
        /// <summary>Legacy key for income verification flag</summary>
        public const string IncomeVerification = "income_verification";
        
        /// <summary>Legacy key for credit report flag</summary>
        public const string CreditReport = "credit_report";
        
        /// <summary>Legacy key for property appraisal flag</summary>
        public const string PropertyAppraisal = "property_appraisal";
        
        /// <summary>Legacy key for employment verification flag</summary>
        public const string EmploymentVerification = "employment_verification";
        
        /// <summary>Legacy key for annual income</summary>
        public const string AnnualIncome = "annual_income";
        
        /// <summary>Legacy key for loan amount</summary>
        public const string LoanAmount = "loan_amount";
    }
}

/// <summary>
/// Strongly typed data transfer objects for mortgage requirement data
/// These DTOs can be used for validation and documentation purposes
/// </summary>
public class MortgageIncomeDataDto
{
    /// <summary>Annual income in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Annual income must be positive")]
    public decimal AnnualIncome { get; set; }

    /// <summary>Type of employment</summary>
    [Required]
    [RegularExpression("^(full-time|part-time|contract|self-employed)$", 
        ErrorMessage = "Employment type must be one of: full-time, part-time, contract, self-employed")]
    public string EmploymentType { get; set; } = string.Empty;

    /// <summary>Years of employment (supports decimals for partial years)</summary>
    [Range(0, 50, ErrorMessage = "Years employed must be between 0 and 50")]
    public decimal YearsEmployed { get; set; }
}

public class MortgageCreditDataDto
{
    /// <summary>Credit score (300-850 range)</summary>
    [Range(300, 850, ErrorMessage = "Credit score must be between 300 and 850")]
    public int CreditScore { get; set; }

    /// <summary>Date of credit report</summary>
    [Required]
    public DateTime ReportDate { get; set; }

    /// <summary>Outstanding debts in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Outstanding debts must be positive")]
    public decimal OutstandingDebts { get; set; }
}

public class MortgageEmploymentDataDto
{
    /// <summary>Name of current employer</summary>
    [Required]
    [StringLength(200, ErrorMessage = "Employer name cannot exceed 200 characters")]
    public string EmployerName { get; set; } = string.Empty;

    /// <summary>Current job title</summary>
    [Required]
    [StringLength(200, ErrorMessage = "Job title cannot exceed 200 characters")]
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>Monthly salary in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Monthly salary must be positive")]
    public decimal MonthlySalary { get; set; }

    /// <summary>Whether employment has been verified</summary>
    public bool IsVerified { get; set; }
}

public class MortgagePropertyDataDto
{
    /// <summary>Appraised property value in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Property value must be positive")]
    public decimal PropertyValue { get; set; }

    /// <summary>Requested loan amount in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Loan amount must be positive")]
    public decimal LoanAmount { get; set; }

    /// <summary>Type of property</summary>
    [Required]
    [RegularExpression("^(single-family|condo|townhouse|multi-family)$", 
        ErrorMessage = "Property type must be one of: single-family, condo, townhouse, multi-family")]
    public string PropertyType { get; set; } = string.Empty;

    /// <summary>Date of property appraisal</summary>
    [Required]
    public DateTime AppraisalDate { get; set; }

    /// <summary>Whether appraisal has been completed</summary>
    public bool AppraisalCompleted { get; set; }
}

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

    /// <summary>
    /// Evaluates the mortgage request status based on available data
    /// </summary>
    /// <param name="request">The mortgage request to evaluate</param>
    private void EvaluateRequestStatus(MortgageRequest request)
    {
        var data = request.RequestData;
        var missingRequirements = new List<string>();

        // Check for required income data
        bool hasIncomeData = data.ContainsKey(MortgageDataFields.Income.Annual) || 
                           data.ContainsKey(MortgageDataFields.Legacy.IncomeVerification);
        if (!hasIncomeData)
            missingRequirements.Add("Income verification");

        // Check for required credit data
        bool hasCreditData = data.ContainsKey(MortgageDataFields.Credit.Score) || 
                           data.ContainsKey(MortgageDataFields.Legacy.CreditReport);
        if (!hasCreditData)
            missingRequirements.Add("Credit report");

        // Check for required property data
        bool hasPropertyData = data.ContainsKey(MortgageDataFields.Property.Value) || 
                             data.ContainsKey(MortgageDataFields.Legacy.PropertyAppraisal);
        if (!hasPropertyData)
            missingRequirements.Add("Property appraisal");

        // Check for required employment data
        bool hasEmploymentData = data.ContainsKey(MortgageDataFields.Employment.Employer) || 
                               data.ContainsKey(MortgageDataFields.Legacy.EmploymentVerification);
        if (!hasEmploymentData)
            missingRequirements.Add("Employment verification");

        if (missingRequirements.Count == 0)
        {
            // All basic requirements met - perform approval logic
            var income = GetValueAsDecimal(data, MortgageDataFields.Income.Annual) ?? 
                        GetValueAsDecimal(data, MortgageDataFields.Legacy.AnnualIncome);
            var creditScore = GetValueAsInt(data, MortgageDataFields.Credit.Score);
            var loanAmount = GetValueAsDecimal(data, MortgageDataFields.Property.LoanAmount) ?? 
                           GetValueAsDecimal(data, MortgageDataFields.Legacy.LoanAmount);

            if (income > 0 && creditScore > 0 && loanAmount > 0)
            {
                // Calculate debt-to-income ratio (monthly loan payment vs monthly income)
                var monthlyIncome = income / 12;
                // Assume 30-year mortgage at 7% interest for payment calculation
                var monthlyPayment = CalculateMonthlyMortgagePayment(loanAmount.Value, 0.07m, 30);
                var debtToIncomeRatio = monthlyPayment / monthlyIncome;
                
                if (creditScore >= 650 && debtToIncomeRatio <= 0.43m)
                {
                    request.Status = MortgageRequestStatus.Approved;
                    request.StatusReason = $"Application approved - Credit score: {creditScore}, DTI ratio: {debtToIncomeRatio:P2}";
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
                request.StatusReason = "All documents received - under manual review for missing financial data";
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

    /// <summary>
    /// Calculates monthly mortgage payment using standard amortization formula
    /// </summary>
    /// <param name="principal">Loan amount</param>
    /// <param name="annualRate">Annual interest rate (as decimal, e.g., 0.07 for 7%)</param>
    /// <param name="years">Loan term in years</param>
    /// <returns>Monthly payment amount</returns>
    private decimal CalculateMonthlyMortgagePayment(decimal principal, decimal annualRate, int years)
    {
        if (annualRate == 0) return principal / (years * 12); // No interest
        
        var monthlyRate = annualRate / 12;
        var numberOfPayments = years * 12;
        
        // M = P * [r(1+r)^n] / [(1+r)^n - 1]
        var monthlyRateCompounded = (decimal)Math.Pow((double)(1 + monthlyRate), numberOfPayments);
        return principal * (monthlyRate * monthlyRateCompounded) / (monthlyRateCompounded - 1);
    }

    /// <summary>
    /// Safely retrieves a decimal value from the data dictionary
    /// </summary>
    /// <param name="data">Data dictionary</param>
    /// <param name="key">Key to lookup</param>
    /// <returns>Decimal value if found and parseable, null otherwise</returns>
    private decimal? GetValueAsDecimal(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (decimal.TryParse(value?.ToString(), out var result))
                return result;
        }
        return null;
    }

    /// <summary>
    /// Safely retrieves an integer value from the data dictionary
    /// </summary>
    /// <param name="data">Data dictionary</param>
    /// <param name="key">Key to lookup</param>
    /// <returns>Integer value if found and parseable, 0 otherwise</returns>
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
