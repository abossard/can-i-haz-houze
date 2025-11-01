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
 * This service manages mortgage applications with strongly-typed data collection and automated approval logic.
 * 
 * Data Structure Overview:
 * ======================
 * 
 * Mortgage requests store data using strongly-typed models (RequestData property) containing:
 * 
 * Income Verification Data (MortgageIncomeData):
 * - AnnualIncome: Annual income in dollars (decimal?)
 * - EmploymentType: Employment type (string: full-time, part-time, contract, self-employed)
 * - YearsEmployed: Years of employment (decimal?, supports partial years)
 * 
 * Credit Report Data (MortgageCreditData):
 * - Score: Credit score (int?, range 300-850)
 * - ReportDate: Date of credit report (DateTime?)
 * - OutstandingDebts: Outstanding debts in dollars (decimal?)
 * 
 * Employment Verification Data (MortgageEmploymentData):
 * - EmployerName: Name of current employer (string)
 * - JobTitle: Current job title (string)
 * - MonthlySalary: Monthly salary in dollars (decimal?)
 * - IsVerified: Whether employment has been verified (bool)
 * 
 * Property Appraisal Data (MortgagePropertyData):
 * - PropertyValue: Appraised property value in dollars (decimal?)
 * - LoanAmount: Requested loan amount in dollars (decimal?)
 * - PropertyType: Type of property (string: single-family, condo, townhouse, multi-family)
 * - AppraisalDate: Date of property appraisal (DateTime?)
 * - AppraisalCompleted: Whether appraisal has been completed (bool)
 * 
 * Approval Logic:
 * ==============
 * 
 * 1. All four requirement categories must have data present
 * 2. Credit score must be >= 650
 * 3. Debt-to-income ratio (monthly payment / monthly income) must be <= 43%
 * 4. Monthly payment is calculated using standard 30-year mortgage at 7% interest
 * 5. Cross-service verification must pass (documents and financial verification)
 * 
 * Status Flow:
 * ===========
 * 
 * Pending -> RequiresAdditionalInfo -> UnderReview -> Approved/Rejected
 * 
 * - Pending: Initial state when request is created
 * - RequiresAdditionalInfo: Missing one or more requirement categories
 * - UnderReview: All requirements present but insufficient financial data for auto-approval
 * - Approved: Meets all automated approval criteria including cross-service verification
 * - Rejected: Fails automated approval criteria (low credit score, high DTI ratio, or verification failure)
 */

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddOpenApiWithAzureContainerAppsServers();

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

app.MapOpenApi();
app.MapScalarApiReference();

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
        var dto = MortgageRequestDto.FromDomain(mortgageRequest);
        return Results.Created($"/mortgage-requests/{mortgageRequest.RequestId}", dto);
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
        if (mortgageRequest == null) return Results.NotFound();
        
        var dto = MortgageRequestDto.FromDomain(mortgageRequest);
        return Results.Ok(dto);
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
        if (mortgageRequest == null) return Results.NotFound();
        
        var dto = MortgageRequestDto.FromDomain(mortgageRequest);
        return Results.Ok(dto);
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
    [FromBody, Required] UpdateMortgageDataStrongDto updateData,
    IMortgageApprovalService mortgageService) =>
{
    try
    {
        var mortgageRequest = await mortgageService.UpdateMortgageDataStrongAsync(requestId, updateData);
        if (mortgageRequest == null) return Results.NotFound();
        
        var dto = MortgageRequestDto.FromDomain(mortgageRequest);
        return Results.Ok(dto);
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
.WithTags("MortgageRequests")
.WithSummary("Update mortgage request with strongly-typed data")
.WithDescription("Updates mortgage request data using strongly-typed DTOs for better type safety and validation.");

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
        var dtos = requests.Select(MortgageRequestDto.FromDomain).ToList();
        return Results.Ok(dtos);
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

        // Trigger re-evaluation by updating with current strongly-typed data (this forces status evaluation)
        var emptyUpdate = new UpdateMortgageDataStrongDto();
        var updatedRequest = await mortgageService.UpdateMortgageDataStrongAsync(requestId, emptyUpdate);
        
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

app.MapDefaultEndpoints();

// Register MCP tools for MortgageApprover
var mcpServer = app.Services.GetRequiredService<IMCPServer>();
var serviceProvider = app.Services;

// Register create mortgage request tool
mcpServer.RegisterTool<CreateMortgageRequestMCPRequest>("create_mortgage_request",
    "Create a new mortgage application request",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var mortgageService = scope.ServiceProvider.GetRequiredService<IMortgageApprovalService>();
        var mortgageRequest = await mortgageService.CreateMortgageRequestAsync(req.UserName);
        return MortgageRequestDto.FromDomain(mortgageRequest);
    });

// Register get mortgage request tool
mcpServer.RegisterTool<GetMortgageRequestRequest>("get_mortgage_request",
    "Retrieve mortgage application details by request ID",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var mortgageService = scope.ServiceProvider.GetRequiredService<IMortgageApprovalService>();
        var mortgageRequest = await mortgageService.GetMortgageRequestAsync(req.RequestId);
        return mortgageRequest != null ? MortgageRequestDto.FromDomain(mortgageRequest) : null;
    });

// Register get mortgage request by user tool
mcpServer.RegisterTool<GetMortgageRequestByUserRequest>("get_mortgage_request_by_user",
    "Retrieve mortgage application details by username",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var mortgageService = scope.ServiceProvider.GetRequiredService<IMortgageApprovalService>();
        var mortgageRequest = await mortgageService.GetMortgageRequestByUserAsync(req.UserName);
        return mortgageRequest != null ? MortgageRequestDto.FromDomain(mortgageRequest) : null;
    });

// Register update mortgage data tool
mcpServer.RegisterTool<UpdateMortgageDataMCPRequest>("update_mortgage_data",
    "Update mortgage application data sections",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var mortgageService = scope.ServiceProvider.GetRequiredService<IMortgageApprovalService>();
        var updateData = new UpdateMortgageDataStrongDto
        {
            Income = req.Income,
            Credit = req.Credit,
            Employment = req.Employment,
            Property = req.Property
        };
        
        var mortgageRequest = await mortgageService.UpdateMortgageDataStrongAsync(req.RequestId, updateData);
        return mortgageRequest != null ? MortgageRequestDto.FromDomain(mortgageRequest) : null;
    });

// Register cross-service verification tool
mcpServer.RegisterTool<VerifyMortgageRequestRequest>("verify_mortgage_request",
    "Trigger cross-service verification for mortgage request including document and ledger checks",
    async req => 
    {
        using var scope = serviceProvider.CreateScope();
        var verificationService = scope.ServiceProvider.GetRequiredService<ICrossServiceVerificationService>();
        var mortgageService = scope.ServiceProvider.GetRequiredService<IMortgageApprovalService>();
        var mortgageRequest = await mortgageService.GetMortgageRequestAsync(req.RequestId);
        if (mortgageRequest == null) return null;
        
        return await verificationService.VerifyMortgageRequirementsAsync(mortgageRequest.UserName, mortgageRequest.RequestData);
    });

// Register MCP resources for MortgageApprover
mcpServer.RegisterResource("mortgage://requests/summary", "Mortgage Requests Summary", 
    "Summary of all mortgage requests in the system",
    async () => new { message = "Mortgage requests summary resource - specify user parameter for user-specific requests" });

app.Logger.LogInformation("Registered MCP tools and resources for MortgageApprover");

app.Run();

// DTOs for API endpoints
public record CreateMortgageRequestDto(string UserName);

/// <summary>
/// Strongly-typed DTO for updating mortgage data sections
/// </summary>
public class UpdateMortgageDataStrongDto
{
    public MortgageIncomeData? Income { get; set; }
    public MortgageCreditData? Credit { get; set; }
    public MortgageEmploymentData? Employment { get; set; }
    public MortgagePropertyData? Property { get; set; }
}

/// <summary>
/// DTO for API responses with strongly-typed mortgage request data
/// </summary>
public class MortgageRequestDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusReason { get; set; } = string.Empty;
    public string MissingRequirements { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Strongly-typed mortgage request data
    /// </summary>
    public MortgageRequestData RequestData { get; set; } = new();
    
    public static MortgageRequestDto FromDomain(MortgageRequest request)
    {
        return new MortgageRequestDto
        {
            Id = request.RequestId,
            UserName = request.UserName,
            Status = request.Status.ToString(),
            StatusReason = request.StatusReason,
            MissingRequirements = request.MissingRequirements,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.UpdatedAt,
            RequestData = request.RequestData
        };
    }
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
    Task<CrossServiceVerificationResult> VerifyMortgageRequirementsAsync(string userName, MortgageRequestData mortgageData);
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

    public async Task<CrossServiceVerificationResult> VerifyMortgageRequirementsAsync(string userName, MortgageRequestData mortgageData)
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
            var loanAmount = mortgageData.Property.LoanAmount;
            
            if (loanAmount.HasValue && loanAmount.Value > 0)
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
    public string Type { get; set; } = "mortgage"; // Document type discriminator
    
    // Strongly-typed mortgage data
    public MortgageRequestData RequestData { get; set; } = new();
}

/// <summary>
/// Strongly-typed mortgage request data model
/// </summary>
public class MortgageRequestData
{
    public MortgageIncomeData Income { get; set; } = new();
    public MortgageCreditData Credit { get; set; } = new();
    public MortgageEmploymentData Employment { get; set; } = new();
    public MortgagePropertyData Property { get; set; } = new();
}

/// <summary>
/// Income verification data
/// </summary>
public class MortgageIncomeData
{
    /// <summary>Annual income in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Annual income must be positive")]
    public decimal? AnnualIncome { get; set; }

    /// <summary>Type of employment</summary>
    [RegularExpression("^(full-time|part-time|contract|self-employed|)$", 
        ErrorMessage = "Employment type must be one of: full-time, part-time, contract, self-employed")]
    public string EmploymentType { get; set; } = string.Empty;

    /// <summary>Years of employment (supports decimals for partial years)</summary>
    [Range(0, 50, ErrorMessage = "Years employed must be between 0 and 50")]
    public decimal? YearsEmployed { get; set; }
}

/// <summary>
/// Credit report data
/// </summary>
public class MortgageCreditData
{
    /// <summary>Credit score (300-850 range)</summary>
    [Range(300, 850, ErrorMessage = "Credit score must be between 300 and 850")]
    public int? Score { get; set; }

    /// <summary>Date of credit report</summary>
    public DateTime? ReportDate { get; set; }

    /// <summary>Outstanding debts in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Outstanding debts must be positive")]
    public decimal? OutstandingDebts { get; set; }
}

/// <summary>
/// Employment verification data
/// </summary>
public class MortgageEmploymentData
{
    /// <summary>Name of current employer</summary>
    [StringLength(200, ErrorMessage = "Employer name cannot exceed 200 characters")]
    public string EmployerName { get; set; } = string.Empty;

    /// <summary>Current job title</summary>
    [StringLength(200, ErrorMessage = "Job title cannot exceed 200 characters")]
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>Monthly salary in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Monthly salary must be positive")]
    public decimal? MonthlySalary { get; set; }

    /// <summary>Whether employment has been verified</summary>
    public bool IsVerified { get; set; }
}

/// <summary>
/// Property appraisal data
/// </summary>
public class MortgagePropertyData
{
    /// <summary>Appraised property value in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Property value must be positive")]
    public decimal? PropertyValue { get; set; }

    /// <summary>Requested loan amount in dollars</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Loan amount must be positive")]
    public decimal? LoanAmount { get; set; }

    /// <summary>Type of property</summary>
    [RegularExpression("^(single-family|condo|townhouse|multi-family|)$", 
        ErrorMessage = "Property type must be one of: single-family, condo, townhouse, multi-family")]
    public string PropertyType { get; set; } = string.Empty;

    /// <summary>Date of property appraisal</summary>
    public DateTime? AppraisalDate { get; set; }

    /// <summary>Whether appraisal has been completed</summary>
    public bool AppraisalCompleted { get; set; }
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
    Task<MortgageRequest?> UpdateMortgageDataStrongAsync(Guid requestId, UpdateMortgageDataStrongDto updateData);
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

    public async Task<MortgageRequest?> UpdateMortgageDataStrongAsync(Guid requestId, UpdateMortgageDataStrongDto updateData)
    {
        try
        {
            // First find the mortgage request
            var mortgageRequest = await GetMortgageRequestAsync(requestId);
            if (mortgageRequest == null) return null;

            // Update the strongly-typed data directly
            if (updateData.Income != null)
            {
                mortgageRequest.RequestData.Income = updateData.Income;
            }
            if (updateData.Credit != null)
            {
                mortgageRequest.RequestData.Credit = updateData.Credit;
            }
            if (updateData.Employment != null)
            {
                mortgageRequest.RequestData.Employment = updateData.Employment;
            }
            if (updateData.Property != null)
            {
                mortgageRequest.RequestData.Property = updateData.Property;
            }
            
            mortgageRequest.UpdatedAt = DateTime.UtcNow;

            // Evaluate status based on the updated data (includes cross-service verification)
            await EvaluateRequestStatusAsync(mortgageRequest);

            // Update the document in Cosmos DB
            await _container.ReplaceItemAsync(mortgageRequest, mortgageRequest.id, new PartitionKey(mortgageRequest.UserName));

            _logger.LogInformation("Updated mortgage request {RequestId} with strongly-typed data. Status: {Status}", 
                requestId, mortgageRequest.Status);
            
            return mortgageRequest;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error updating mortgage request {RequestId} with strongly-typed data", requestId);
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
            // Build query string with all conditions
            var queryBuilder = "SELECT * FROM c WHERE c.Type = @type";
            var queryDefinition = new QueryDefinition(queryBuilder)
                .WithParameter("@type", "mortgage");

            // Add status filter if provided
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MortgageRequestStatus>(status, true, out var statusEnum))
            {
                queryBuilder += " AND c.Status = @status";
                queryDefinition = new QueryDefinition(queryBuilder)
                    .WithParameter("@type", "mortgage")
                    .WithParameter("@status", statusEnum.ToString());
            }

            // Add pagination - build final query with all parameters
            queryBuilder += " ORDER BY c.UpdatedAt DESC OFFSET @skip LIMIT @take";
            var finalQuery = new QueryDefinition(queryBuilder)
                .WithParameter("@type", "mortgage")
                .WithParameter("@skip", (page - 1) * pageSize)
                .WithParameter("@take", pageSize);

            // Re-add status parameter if it was specified
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<MortgageRequestStatus>(status, true, out statusEnum))
            {
                finalQuery = finalQuery.WithParameter("@status", statusEnum.ToString());
            }

            var iterator = _container.GetItemQueryIterator<MortgageRequest>(finalQuery);
            var requests = new List<MortgageRequest>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                requests.AddRange(response);
            }

            _logger.LogInformation("Retrieved {Count} mortgage requests", requests.Count);
            return requests;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Error retrieving mortgage requests");
            return new List<MortgageRequest>();
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

        // Check for required data fields using strongly-typed properties
        bool hasIncomeData = data.Income.AnnualIncome.HasValue;
        if (!hasIncomeData)
            missingRequirements.Add("Income verification");

        bool hasCreditData = data.Credit.Score.HasValue;
        if (!hasCreditData)
            missingRequirements.Add("Credit report");

        bool hasPropertyData = data.Property.PropertyValue.HasValue && data.Property.LoanAmount.HasValue;
        if (!hasPropertyData)
            missingRequirements.Add("Property appraisal");

        bool hasEmploymentData = !string.IsNullOrEmpty(data.Employment.EmployerName);
        if (!hasEmploymentData)
            missingRequirements.Add("Employment verification");

        if (missingRequirements.Count == 0)
        {
            // All basic requirements met - perform cross-service verification
            try
            {
                var crossServiceResult = await _crossServiceVerification.VerifyMortgageRequirementsAsync(request.UserName, data);
                
                if (!crossServiceResult.AllVerificationsPassed)
                {
                    request.Status = MortgageRequestStatus.Rejected;
                    request.StatusReason = $"Cross-service verification failed: {string.Join(", ", crossServiceResult.FailureReasons)}";
                    request.MissingRequirements = string.Empty;
                    return;
                }

                // Perform financial approval logic using strongly-typed properties
                var income = data.Income.AnnualIncome;
                var creditScore = data.Credit.Score;
                var loanAmount = data.Property.LoanAmount;

                if (income.HasValue && creditScore.HasValue && loanAmount.HasValue && income > 0 && creditScore > 0 && loanAmount > 0)
                {
                    // Calculate debt-to-income ratio (monthly loan payment vs monthly income)
                    var monthlyIncome = income.Value / 12;
                    // Assume 30-year mortgage at 7% interest for payment calculation
                    var monthlyPayment = CalculateMonthlyMortgagePayment(loanAmount.Value, 0.07m, 30);
                    var debtToIncomeRatio = monthlyPayment / monthlyIncome;
                    
                    // Enhanced approval criteria including cross-service verification
                    bool creditScoreOk = creditScore.Value >= 650;
                    bool dtiOk = debtToIncomeRatio <= 0.43m;
                    bool documentsOk = crossServiceResult.DocumentVerification.AllDocumentsVerified;
                    bool financialsOk = crossServiceResult.FinancialVerification.HasSufficientFunds;
                    
                    if (creditScoreOk && dtiOk && documentsOk && financialsOk)
                    {
                        request.Status = MortgageRequestStatus.Approved;
                        request.StatusReason = $"Application approved - Credit: {creditScore.Value}, DTI: {debtToIncomeRatio:P2}, Documents: Verified, Funds: Sufficient";
                        request.MissingRequirements = string.Empty;
                    }
                    else
                    {
                        request.Status = MortgageRequestStatus.Rejected;
                        var reasons = new List<string>();
                        if (!creditScoreOk) reasons.Add($"Credit score too low ({creditScore.Value} < 650)");
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
}

// MCP Tool Request Models for MortgageApprover
/// <summary>
/// Request model for creating mortgage request via MCP
/// </summary>
/// <param name="UserName">Username for mortgage application</param>
public record CreateMortgageRequestMCPRequest(string UserName);

/// <summary>
/// Request model for getting mortgage request via MCP
/// </summary>
/// <param name="RequestId">Unique GUID identifier of the mortgage request</param>
public record GetMortgageRequestRequest(Guid RequestId);

/// <summary>
/// Request model for getting mortgage request by user via MCP
/// </summary>
/// <param name="UserName">Username to search for</param>
public record GetMortgageRequestByUserRequest(string UserName);

/// <summary>
/// Request model for updating mortgage data via MCP
/// </summary>
/// <param name="RequestId">Unique GUID identifier of the mortgage request</param>
/// <param name="Income">Income data to update</param>
/// <param name="Credit">Credit data to update</param>
/// <param name="Employment">Employment data to update</param>
/// <param name="Property">Property data to update</param>
public record UpdateMortgageDataMCPRequest(
    Guid RequestId,
    MortgageIncomeData? Income = null,
    MortgageCreditData? Credit = null,
    MortgageEmploymentData? Employment = null,
    MortgagePropertyData? Property = null
);

/// <summary>
/// Request model for verifying mortgage request via MCP
/// </summary>
/// <param name="RequestId">Unique GUID identifier of the mortgage request</param>
public record VerifyMortgageRequestRequest(Guid RequestId);

// Make Program class accessible for testing
namespace CanIHazHouze.MortgageApprover
{
    public partial class Program { }
}
