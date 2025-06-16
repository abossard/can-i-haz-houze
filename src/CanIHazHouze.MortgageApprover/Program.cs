using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Azure.Cosmos;

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

// Configure JSON serialization to use string values for enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Configure mortgage storage options
builder.Services.Configure<MortgageStorageOptions>(
    builder.Configuration.GetSection("MortgageStorage"));

// Add Azure Cosmos DB using Aspire
builder.AddAzureCosmosClient("cosmos");

// Add mortgage approval service
builder.Services.AddScoped<IMortgageApprovalService, MortgageApprovalServiceImpl>();

// Add HTTP clients for inter-service communication with proper Aspire service discovery
builder.Services.AddHttpClient<DocumentVerificationService>();

builder.Services.AddHttpClient<LedgerVerificationService>();

// Register verification services with proper scoping
builder.Services.AddScoped<IDocumentVerificationService, DocumentVerificationService>();
builder.Services.AddScoped<ILedgerVerificationService, LedgerVerificationService>();
builder.Services.AddScoped<ICrossServiceVerificationService, CrossServiceVerificationService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Use CORS
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
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
        return Results.Created($"/mortgage-requests/{mortgageRequest.RequestId}", mortgageRequest);
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

// Cross-Service Verification API endpoints
app.MapPost("/mortgage-requests/{requestId:guid}/verify", async (
    Guid requestId,
    IMortgageApprovalService mortgageService,
    ICrossServiceVerificationService crossServiceVerification) =>
{
    try
    {
        var mortgageRequest = await mortgageService.GetMortgageRequestAsync(requestId);
        if (mortgageRequest == null)
            return Results.NotFound();

        var verificationResult = await crossServiceVerification.VerifyMortgageRequirementsAsync(
            mortgageRequest.UserName, mortgageRequest.RequestData);
        
        return Results.Ok(verificationResult);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error performing cross-service verification for request {RequestId}", requestId);
        return Results.Problem("An error occurred while performing cross-service verification.");
    }
})
.WithName("VerifyMortgageRequest")
.WithOpenApi()
.WithTags("MortgageRequests")
.WithSummary("Trigger cross-service verification for a mortgage request")
.WithDescription("Manually triggers document and financial verification through external services.");

app.MapPost("/mortgage-requests/{requestId:guid}/refresh-status", async (
    Guid requestId,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var mortgageRequest = await mortgageService.GetMortgageRequestAsync(requestId);
        if (mortgageRequest == null)
            return Results.NotFound();

        // Trigger re-evaluation by updating with current data (this forces status evaluation)
        var updatedRequest = await mortgageService.UpdateMortgageDataAsync(requestId, new Dictionary<string, object>());
        
        return updatedRequest != null ? Results.Ok(updatedRequest) : Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error refreshing status for request {RequestId}", requestId);
        return Results.Problem("An error occurred while refreshing the request status.");
    }
})
.WithName("RefreshMortgageRequestStatus")
.WithOpenApi()
.WithTags("MortgageRequests")
.WithSummary("Refresh mortgage request status")
.WithDescription("Re-evaluates the mortgage request status including cross-service verification.");

app.MapGet("/mortgage-requests/{requestId:guid}/verification-status", async (
    Guid requestId,
    IMortgageApprovalService mortgageService,
    ICrossServiceVerificationService crossServiceVerification) =>
{
    try
    {
        var mortgageRequest = await mortgageService.GetMortgageRequestAsync(requestId);
        if (mortgageRequest == null)
            return Results.NotFound();

        var verificationResult = await crossServiceVerification.VerifyMortgageRequirementsAsync(
            mortgageRequest.UserName, mortgageRequest.RequestData);

        var statusInfo = new
        {
            RequestId = requestId,
            UserName = mortgageRequest.UserName,
            CurrentStatus = mortgageRequest.Status.ToString(),
            StatusReason = mortgageRequest.StatusReason,
            DocumentVerification = new
            {
                AllDocumentsVerified = verificationResult.DocumentVerification.AllDocumentsVerified,
                HasIncomeDocuments = verificationResult.DocumentVerification.HasIncomeDocuments,
                HasCreditReport = verificationResult.DocumentVerification.HasCreditReport,
                HasEmploymentVerification = verificationResult.DocumentVerification.HasEmploymentVerification,
                HasPropertyAppraisal = verificationResult.DocumentVerification.HasPropertyAppraisal,
                DocumentCount = verificationResult.DocumentVerification.Documents.Count
            },
            FinancialVerification = new
            {
                verificationResult.FinancialVerification.AccountExists,
                verificationResult.FinancialVerification.CurrentBalance,
                verificationResult.FinancialVerification.HasSufficientFunds,
                verificationResult.FinancialVerification.IncomeConsistent
            },
            CrossServiceVerification = new
            {
                verificationResult.AllVerificationsPassed,
                FailureReasons = verificationResult.FailureReasons,
                VerificationDate = DateTime.UtcNow,
                ServiceErrors = verificationResult.ServiceErrors.Select(e => new
                {
                    e.ServiceName,
                    e.ErrorMessage,
                    e.Details,
                    e.ErrorTime,
                    e.IsConnectivityError
                }).ToList(),
                HasServiceErrors = verificationResult.ServiceErrors.Any(),
                ConnectivityErrors = verificationResult.ServiceErrors.Count(e => e.IsConnectivityError)
            }
        };
        
        return Results.Ok(statusInfo);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error getting verification status for request {RequestId}", requestId);
        return Results.Problem("An error occurred while getting verification status.");
    }
})
.WithName("GetMortgageVerificationStatus")
.WithOpenApi()
.WithTags("MortgageRequests")
.WithSummary("Get detailed verification status")
.WithDescription("Returns detailed information about document and financial verification status.");

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

// Cross-Service Integration Models and Services
// ============================================

/// <summary>
/// Document verification result from the Document Service
/// </summary>
public class DocumentVerificationResult
{
    public string UserName { get; set; } = string.Empty;
    public List<VerifiedDocument> Documents { get; set; } = new();
    public bool HasIncomeDocuments { get; set; }
    public bool HasCreditReport { get; set; }
    public bool HasEmploymentVerification { get; set; }
    public bool HasPropertyAppraisal { get; set; }
    public int DocumentCount => Documents.Count;
    public bool AllDocumentsVerified => HasIncomeDocuments && HasCreditReport && HasEmploymentVerification && HasPropertyAppraisal;
}

public class VerifiedDocument
{
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

/// <summary>
/// Financial verification result from the Ledger Service
/// </summary>
public class FinancialVerificationResult
{
    public string UserName { get; set; } = string.Empty;
    public bool AccountExists { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal AverageMonthlyIncome { get; set; }
    public List<TransactionSummary> RecentTransactions { get; set; } = new();
    public bool HasSufficientFunds { get; set; }
    public bool IncomeConsistent { get; set; }
    public decimal DebtToIncomeRatio { get; set; }
}

public class TransactionSummary
{
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Combined verification result for mortgage approval
/// </summary>
public class CrossServiceVerificationResult
{
    public DocumentVerificationResult DocumentVerification { get; set; } = new();
    public FinancialVerificationResult FinancialVerification { get; set; } = new();
    public bool AllVerificationsPassed { get; set; }
    public List<string> FailureReasons { get; set; } = new();
    public Dictionary<string, object> AdditionalData { get; set; } = new();
    public List<ServiceError> ServiceErrors { get; set; } = new();
}

/// <summary>
/// Service error information for detailed error reporting
/// </summary>
public class ServiceError
{
    public string ServiceName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime ErrorTime { get; set; } = DateTime.UtcNow;
    public bool IsConnectivityError { get; set; }
}

// Service Interfaces
// =================

/// <summary>
/// Interface for verifying documents through the Document Service
/// </summary>
public interface IDocumentVerificationService
{
    Task<DocumentVerificationResult> GetDocumentVerificationAsync(string userName);
    Task<bool> HasRequiredDocumentsAsync(string userName);
}

/// <summary>
/// Interface for verifying financial data through the Ledger Service
/// </summary>
public interface ILedgerVerificationService
{
    Task<FinancialVerificationResult> GetFinancialVerificationAsync(string userName);
    Task<bool> HasSufficientFundsAsync(string userName, decimal requiredAmount);
}

/// <summary>
/// Interface for cross-service verification coordination
/// </summary>
public interface ICrossServiceVerificationService
{
    Task<CrossServiceVerificationResult> VerifyMortgageRequirementsAsync(string userName, Dictionary<string, object> mortgageData);
}

// Service Implementations
// ======================

/// <summary>
/// Service for communicating with the Document Service
/// </summary>
public class DocumentVerificationService : IDocumentVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DocumentVerificationService> _logger;

    public DocumentVerificationService(HttpClient httpClient, ILogger<DocumentVerificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DocumentVerificationResult> GetDocumentVerificationAsync(string userName)
    {
        try
        {
            _logger.LogInformation("Requesting document verification for user {UserName}", userName);
            _logger.LogInformation("Document service base address: {BaseAddress}", _httpClient.BaseAddress);
            
            var response = await _httpClient.GetAsync($"https+http://documentservice/documents/user/{userName}/verification");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<DocumentVerificationResult>(content, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                _logger.LogInformation("Document verification completed for user {UserName}: {AllVerified}", 
                    userName, result?.AllDocumentsVerified);
                
                return result ?? new DocumentVerificationResult { UserName = userName };
            }
            else
            {
                _logger.LogWarning("Document service returned {StatusCode} for user {UserName}", 
                    response.StatusCode, userName);
                return new DocumentVerificationResult { UserName = userName };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request error getting document verification for user {UserName}. BaseAddress: {BaseAddress}", 
                userName, _httpClient.BaseAddress);
            return new DocumentVerificationResult { UserName = userName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting document verification for user {UserName}. BaseAddress: {BaseAddress}", 
                userName, _httpClient.BaseAddress);
            return new DocumentVerificationResult { UserName = userName };
        }
    }

    public async Task<bool> HasRequiredDocumentsAsync(string userName)
    {
        var verification = await GetDocumentVerificationAsync(userName);
        return verification.HasIncomeDocuments && 
               verification.HasCreditReport && 
               verification.HasEmploymentVerification && 
               verification.HasPropertyAppraisal;
    }
}

/// <summary>
/// Service for communicating with the Ledger Service
/// </summary>
public class LedgerVerificationService : ILedgerVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LedgerVerificationService> _logger;

    public LedgerVerificationService(HttpClient httpClient, ILogger<LedgerVerificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FinancialVerificationResult> GetFinancialVerificationAsync(string userName)
    {
        try
        {
            _logger.LogInformation("Requesting financial verification for user {UserName}", userName);
            _logger.LogInformation("Ledger service base address: {BaseAddress}", _httpClient.BaseAddress);
            
            // Get account information
            var accountResponse = await _httpClient.GetAsync($"https+http://ledgerservice/accounts/{userName}");
            
            if (accountResponse.IsSuccessStatusCode)
            {
                var accountContent = await accountResponse.Content.ReadAsStringAsync();
                var accountData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(accountContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, object>();
                
                var result = new FinancialVerificationResult
                {
                    UserName = userName,
                    AccountExists = true,
                    CurrentBalance = GetDecimalValue(accountData, "balance"),
                    HasSufficientFunds = GetDecimalValue(accountData, "balance") >= 10000m, // Minimum for down payment
                    IncomeConsistent = true // Simplified logic for now
                };
                
                _logger.LogInformation("Financial verification completed for user {UserName}: Balance {Balance}", 
                    userName, result.CurrentBalance);
                
                return result;
            }
            else
            {
                _logger.LogWarning("Ledger service returned {StatusCode} for user {UserName}", 
                    accountResponse.StatusCode, userName);
                return new FinancialVerificationResult { UserName = userName };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request error getting financial verification for user {UserName}. BaseAddress: {BaseAddress}", 
                userName, _httpClient.BaseAddress);
            return new FinancialVerificationResult { UserName = userName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting financial verification for user {UserName}. BaseAddress: {BaseAddress}", 
                userName, _httpClient.BaseAddress);
            return new FinancialVerificationResult { UserName = userName };
        }
    }

    public async Task<bool> HasSufficientFundsAsync(string userName, decimal requiredAmount)
    {
        var verification = await GetFinancialVerificationAsync(userName);
        return verification.CurrentBalance >= requiredAmount;
    }

    private decimal GetDecimalValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value) && decimal.TryParse(value?.ToString(), out var result))
            return result;
        return 0;
    }
}

/// <summary>
/// Service for coordinating cross-service verification
/// </summary>
public class CrossServiceVerificationService : ICrossServiceVerificationService
{
    private readonly IDocumentVerificationService _documentService;
    private readonly ILedgerVerificationService _ledgerService;
    private readonly ILogger<CrossServiceVerificationService> _logger;

    public CrossServiceVerificationService(
        IDocumentVerificationService documentService,
        ILedgerVerificationService ledgerService,
        ILogger<CrossServiceVerificationService> logger)
    {
        _documentService = documentService;
        _ledgerService = ledgerService;
        _logger = logger;
    }

    public async Task<CrossServiceVerificationResult> VerifyMortgageRequirementsAsync(string userName, Dictionary<string, object> mortgageData)
    {
        _logger.LogInformation("Starting cross-service verification for user {UserName}", userName);
        
        var result = new CrossServiceVerificationResult();
        var failureReasons = new List<string>();
        var serviceErrors = new List<ServiceError>();

        try
        {
            // Get document verification
            try
            {
                result.DocumentVerification = await _documentService.GetDocumentVerificationAsync(userName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during document verification for user {UserName}", userName);
                serviceErrors.Add(new ServiceError
                {
                    ServiceName = "DocumentService",
                    ErrorMessage = "Document verification service unavailable",
                    Details = ex.Message,
                    IsConnectivityError = ex is HttpRequestException
                });
                failureReasons.Add("Document service unavailable");
            }
            
            // Get financial verification
            try
            {
                result.FinancialVerification = await _ledgerService.GetFinancialVerificationAsync(userName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during financial verification for user {UserName}", userName);
                serviceErrors.Add(new ServiceError
                {
                    ServiceName = "LedgerService",
                    ErrorMessage = "Financial verification service unavailable",
                    Details = ex.Message,
                    IsConnectivityError = ex is HttpRequestException
                });
                failureReasons.Add("Financial service unavailable");
            }

            // Check document requirements
            if (!result.DocumentVerification.AllDocumentsVerified)
            {
                failureReasons.Add("Document verification incomplete");
            }

            // Check financial requirements
            if (!result.FinancialVerification.AccountExists)
            {
                failureReasons.Add("No financial account found");
            }

            // Cross-validate mortgage data with external services
            var loanAmount = GetDecimalValue(mortgageData, MortgageDataFields.Property.LoanAmount) ?? 
                           GetDecimalValue(mortgageData, MortgageDataFields.Legacy.LoanAmount);
            
            if (loanAmount.HasValue)
            {
                var requiredDownPayment = loanAmount.Value * 0.20m; // 20% down payment
                if (result.FinancialVerification.CurrentBalance < requiredDownPayment)
                {
                    failureReasons.Add($"Insufficient funds for down payment. Required: {requiredDownPayment:C}, Available: {result.FinancialVerification.CurrentBalance:C}");
                }
            }

            // Store additional cross-service data
            result.AdditionalData["document_verification_date"] = DateTime.UtcNow;
            result.AdditionalData["financial_verification_date"] = DateTime.UtcNow;
            result.AdditionalData["cross_service_verification"] = true;
            result.AdditionalData["service_errors_count"] = serviceErrors.Count;

            result.FailureReasons = failureReasons;
            result.ServiceErrors = serviceErrors;
            result.AllVerificationsPassed = failureReasons.Count == 0;

            _logger.LogInformation("Cross-service verification completed for user {UserName}: {Passed}, Errors: {ErrorCount}", 
                userName, result.AllVerificationsPassed, serviceErrors.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cross-service verification for user {UserName}", userName);
            result.FailureReasons.Add("Cross-service verification failed due to technical error");
            result.ServiceErrors.Add(new ServiceError
            {
                ServiceName = "CrossServiceVerification",
                ErrorMessage = "Cross-service verification system error",
                Details = ex.Message,
                IsConnectivityError = false
            });
            result.AllVerificationsPassed = false;
            return result;
        }
    }

    private decimal? GetDecimalValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value) && decimal.TryParse(value?.ToString(), out var result))
            return result;
        return null;
    }
}

// Configuration options
public class MortgageStorageOptions
{
    public string BaseDirectory { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "MortgageData_Dev");
}

// Domain Models
public class MortgageRequest
{
    public string id { get; set; } = string.Empty; // Cosmos DB id property
    public string owner { get; set; } = string.Empty; // Partition key (username)
    public Guid RequestId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public MortgageRequestStatus Status { get; set; } = MortgageRequestStatus.Pending;
    public string StatusReason { get; set; } = "Application submitted - awaiting documentation";
    public string MissingRequirements { get; set; } = "All required documentation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string RequestDataJson { get; set; } = "{}"; // Store additional data as JSON
    public string Type { get; set; } = "mortgage"; // Document type discriminator
    
    // Navigation property for additional data
    [JsonIgnore]
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
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<MortgageApprovalServiceImpl> _logger;
    private readonly ICrossServiceVerificationService _crossServiceVerification;
    private readonly Microsoft.Azure.Cosmos.Container _container;

    public MortgageApprovalServiceImpl(
        CosmosClient cosmosClient, 
        ILogger<MortgageApprovalServiceImpl> logger,
        ICrossServiceVerificationService crossServiceVerification)
    {
        _cosmosClient = cosmosClient;
        _logger = logger;
        _crossServiceVerification = crossServiceVerification;
        _container = _cosmosClient.GetContainer("houze", "mortgages");
    }

    public async Task<MortgageRequest> CreateMortgageRequestAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Username cannot be empty", nameof(userName));

        // Check if user already has a request
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.owner = @userName AND c.Type = @type")
                .WithParameter("@userName", userName)
                .WithParameter("@type", "mortgage");

            var iterator = _container.GetItemQueryIterator<MortgageRequest>(query);
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                if (response.Count > 0)
                {
                    throw new InvalidOperationException($"User {userName} already has an existing mortgage request");
                }
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // No existing request found, proceed with creation
        }

        var mortgageRequest = new MortgageRequest
        {
            id = $"mortgage:{Guid.NewGuid()}",
            owner = userName,
            RequestId = Guid.NewGuid(),
            UserName = userName,
            Status = MortgageRequestStatus.Pending,
            StatusReason = "Application submitted - awaiting documentation",
            MissingRequirements = "Income verification, Credit report, Property appraisal, Employment verification",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Type = "mortgage"
        };

        await _container.CreateItemAsync(mortgageRequest, new PartitionKey(userName));

        _logger.LogInformation("Created mortgage request {RequestId} for user {UserName}", mortgageRequest.RequestId, userName);
        return mortgageRequest;
    }

    public async Task<MortgageRequest?> GetMortgageRequestAsync(Guid requestId)
    {
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.RequestId = @requestId AND c.Type = @type")
                .WithParameter("@requestId", requestId)
                .WithParameter("@type", "mortgage");

            var iterator = _container.GetItemQueryIterator<MortgageRequest>(query);
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                return response.FirstOrDefault();
            }
            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MortgageRequest?> GetMortgageRequestByUserAsync(string userName)
    {
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.owner = @userName AND c.Type = @type")
                .WithParameter("@userName", userName)
                .WithParameter("@type", "mortgage");

            var iterator = _container.GetItemQueryIterator<MortgageRequest>(query);
            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                return response.FirstOrDefault();
            }
            return null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MortgageRequest?> UpdateMortgageDataAsync(Guid requestId, Dictionary<string, object> newData)
    {
        try
        {
            // First find the mortgage request
            var mortgageRequest = await GetMortgageRequestAsync(requestId);
            if (mortgageRequest == null) return null;

            // Merge new data with existing data
            var currentData = mortgageRequest.RequestData;
            foreach (var kvp in newData)
            {
                currentData[kvp.Key] = kvp.Value;
            }
            mortgageRequest.RequestData = currentData;
            mortgageRequest.UpdatedAt = DateTime.UtcNow;

            // Evaluate status based on the updated data (includes cross-service verification)
            await EvaluateRequestStatusAsync(mortgageRequest);

            // Update the document in Cosmos DB
            await _container.ReplaceItemAsync(mortgageRequest, mortgageRequest.id, new PartitionKey(mortgageRequest.UserName));

            _logger.LogInformation("Updated mortgage request {RequestId} with new data. Status: {Status}", 
                requestId, mortgageRequest.Status);
            
            return mortgageRequest;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error updating mortgage request {RequestId}", requestId);
            throw;
        }
    }

    public async Task<bool> DeleteMortgageRequestAsync(Guid requestId)
    {
        try
        {
            var mortgageRequest = await GetMortgageRequestAsync(requestId);
            if (mortgageRequest == null) return false;

            await _container.DeleteItemAsync<MortgageRequest>(mortgageRequest.id, new PartitionKey(mortgageRequest.UserName));

            _logger.LogInformation("Deleted mortgage request {RequestId}", requestId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error deleting mortgage request {RequestId}", requestId);
            throw;
        }
    }

    public async Task<IEnumerable<MortgageRequest>> GetMortgageRequestsAsync(int page, int pageSize, string? status)
    {
        try
        {
            var queryBuilder = "SELECT * FROM c WHERE c.Type = @type";
            var queryDefinition = new QueryDefinition(queryBuilder)
                .WithParameter("@type", "mortgage");

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MortgageRequestStatus>(status, true, out var statusEnum))
            {
                queryBuilder += " AND c.Status = @status";
                queryDefinition = new QueryDefinition(queryBuilder)
                    .WithParameter("@type", "mortgage")
                    .WithParameter("@status", statusEnum.ToString());
            }

            queryBuilder += " ORDER BY c.UpdatedAt DESC OFFSET @skip LIMIT @take";
            queryDefinition = new QueryDefinition(queryBuilder)
                .WithParameter("@type", "mortgage")
                .WithParameter("@skip", (page - 1) * pageSize)
                .WithParameter("@take", pageSize);

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MortgageRequestStatus>(status, true, out statusEnum))
            {
                queryDefinition = queryDefinition.WithParameter("@status", statusEnum.ToString());
            }

            var iterator = _container.GetItemQueryIterator<MortgageRequest>(queryDefinition);
            var requests = new List<MortgageRequest>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                requests.AddRange(response);
            }

            return requests;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving mortgage requests");
            throw;
        }
    }

    /// <summary>
    /// Evaluates the mortgage request status based on available data and cross-service verification
    /// </summary>
    /// <param name="request">The mortgage request to evaluate</param>
    private async Task EvaluateRequestStatusAsync(MortgageRequest request)
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
            // All basic requirements met - perform cross-service verification
            try
            {
                var crossServiceResult = await _crossServiceVerification.VerifyMortgageRequirementsAsync(request.UserName, data);
                
                // Merge cross-service data into mortgage request
                foreach (var kvp in crossServiceResult.AdditionalData)
                {
                    data[kvp.Key] = kvp.Value;
                }
                request.RequestData = data;

                if (!crossServiceResult.AllVerificationsPassed)
                {
                    request.Status = MortgageRequestStatus.Rejected;
                    request.StatusReason = $"Cross-service verification failed: {string.Join(", ", crossServiceResult.FailureReasons)}";
                    request.MissingRequirements = string.Empty;
                    return;
                }

                // Perform financial approval logic
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
                    
                    // Enhanced approval criteria including cross-service verification
                    bool creditScoreOk = creditScore >= 650;
                    bool dtiOk = debtToIncomeRatio <= 0.43m;
                    bool documentsOk = crossServiceResult.DocumentVerification.AllDocumentsVerified;
                    bool financialsOk = crossServiceResult.FinancialVerification.HasSufficientFunds;
                    
                    if (creditScoreOk && dtiOk && documentsOk && financialsOk)
                    {
                        request.Status = MortgageRequestStatus.Approved;
                        request.StatusReason = $"Application approved - Credit: {creditScore}, DTI: {debtToIncomeRatio:P2}, Documents: Verified, Funds: Sufficient";
                        request.MissingRequirements = string.Empty;
                    }
                    else
                    {
                        request.Status = MortgageRequestStatus.Rejected;
                        var reasons = new List<string>();
                        if (!creditScoreOk) reasons.Add($"Credit score too low ({creditScore} < 650)");
                        if (!dtiOk) reasons.Add($"Debt-to-income ratio too high ({debtToIncomeRatio:P2} > 43%)");
                        if (!documentsOk) reasons.Add("Document verification incomplete");
                        if (!financialsOk) reasons.Add("Insufficient funds for down payment");
                        request.StatusReason = $"Application rejected: {string.Join(", ", reasons)}";
                        request.MissingRequirements = string.Empty;
                    }
                }
                else
                {
                    request.Status = MortgageRequestStatus.UnderReview;
                    request.StatusReason = "Cross-service verification passed - under manual review for missing financial data";
                    request.MissingRequirements = string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cross-service verification for request {RequestId}", request.RequestId);
                request.Status = MortgageRequestStatus.UnderReview;
                request.StatusReason = "Cross-service verification unavailable - under manual review";
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
