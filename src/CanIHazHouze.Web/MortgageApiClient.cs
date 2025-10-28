using System.Text.Json;

namespace CanIHazHouze.Web;

public class MortgageApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MortgageApiClient> _logger;

    public string BaseUrl => _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public MortgageApiClient(HttpClient httpClient, ILogger<MortgageApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MortgageRequestDto?> CreateMortgageRequestAsync(string userName)
    {
        try
        {
            var request = new { UserName = userName };
            var response = await _httpClient.PostAsJsonAsync("/mortgage-requests", request);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, GetJsonSerializerOptions());
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to create mortgage request for user {UserName}. Status: {StatusCode}, Content: {Content}", userName, response.StatusCode, errorContent);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException($"User {userName} already has an existing mortgage request");
            }
            
            throw new HttpRequestException($"API returned {response.StatusCode}: {errorContent}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when creating mortgage request for user {UserName}", userName);
            throw new InvalidOperationException("Failed to parse mortgage request response from API", ex);
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw business logic exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating mortgage request for user {UserName}", userName);
            throw new InvalidOperationException("Unexpected error occurred while creating mortgage request", ex);
        }
    }

    public async Task<MortgageRequestDto?> GetMortgageRequestByUserAsync(string userName)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/mortgage-requests/user/{userName}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, GetJsonSerializerOptions());
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            
            _logger.LogWarning("Failed to get mortgage request for user {UserName}. Status: {StatusCode}", userName, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mortgage request for user {UserName}", userName);
            return null;
        }
    }

    /// <summary>
    /// Updates mortgage request data using strongly-typed DTOs
    /// </summary>
    public async Task<MortgageRequestDto?> UpdateMortgageDataStrongAsync(Guid requestId, UpdateMortgageDataStrongDto updateData)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/mortgage-requests/{requestId}/data", updateData);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, GetJsonSerializerOptions());
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to update mortgage request {RequestId} with strong typing. Status: {StatusCode}, Content: {Content}", 
                requestId, response.StatusCode, errorContent);
            
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                throw new ArgumentException($"Invalid data provided: {errorContent}");
            }
            
            throw new HttpRequestException($"API returned {response.StatusCode}: {errorContent}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization/deserialization error when updating mortgage request {RequestId}", requestId);
            throw new InvalidOperationException("Failed to process mortgage request update", ex);
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions
        }
        catch (ArgumentException)
        {
            throw; // Re-throw validation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating mortgage request {RequestId}", requestId);
            throw new InvalidOperationException("An unexpected error occurred while updating the mortgage request", ex);
        }
    }

    public async Task<List<MortgageRequestDto>> GetMortgageRequestsAsync(int page = 1, int pageSize = 10, string? status = null)
    {
        try
        {
            var query = $"?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(status))
            {
                query += $"&status={status}";
            }
            
            var response = await _httpClient.GetAsync($"/mortgage-requests{query}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Raw API response for mortgage requests: {Json}", json);
                return JsonSerializer.Deserialize<List<MortgageRequestDto>>(json, GetJsonSerializerOptions()) ?? new List<MortgageRequestDto>();
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to get mortgage requests. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
            throw new HttpRequestException($"API returned {response.StatusCode}: {errorContent}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error when getting mortgage requests");
            throw new InvalidOperationException("Failed to parse mortgage requests response from API", ex);
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting mortgage requests");
            throw new InvalidOperationException("Unexpected error occurred while getting mortgage requests", ex);
        }
    }

    public async Task<bool> DeleteMortgageRequestAsync(Guid requestId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/mortgage-requests/{requestId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting mortgage request {RequestId}", requestId);
            return false;
        }
    }

    public async Task<CrossServiceVerificationResultDto?> VerifyMortgageRequestAsync(Guid requestId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/mortgage-requests/{requestId}/verify", null);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<CrossServiceVerificationResultDto>(json, GetJsonSerializerOptions());
            }
            
            _logger.LogWarning("Failed to verify mortgage request {RequestId}. Status: {StatusCode}", requestId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying mortgage request {RequestId}", requestId);
            return null;
        }
    }

    public async Task<MortgageRequestDto?> RefreshMortgageRequestStatusAsync(Guid requestId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/mortgage-requests/{requestId}/refresh-status", null);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, GetJsonSerializerOptions());
            }
            
            _logger.LogWarning("Failed to refresh mortgage request status {RequestId}. Status: {StatusCode}", requestId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing mortgage request status {RequestId}", requestId);
            return null;
        }
    }

    public async Task<VerificationStatusDto?> GetVerificationStatusAsync(Guid requestId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/mortgage-requests/{requestId}/verification-status");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<VerificationStatusDto>(json, GetJsonSerializerOptions());
            }
            
            _logger.LogWarning("Failed to get verification status for request {RequestId}. Status: {StatusCode}", requestId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting verification status for request {RequestId}", requestId);
            return null;
        }
    }

    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }
}

/// <summary>
/// Strongly-typed DTO for updating mortgage data sections
/// </summary>
public class UpdateMortgageDataStrongDto
{
    public MortgageIncomeDataDto? Income { get; set; }
    public MortgageCreditDataDto? Credit { get; set; }
    public MortgageEmploymentDataDto? Employment { get; set; }
    public MortgagePropertyDataDto? Property { get; set; }
}

public class MortgageRequestDto
{
    public Guid Id { get; set; }
    public Guid RequestId => Id; // Backward compatibility property
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusReason { get; set; } = string.Empty;
    public string MissingRequirements { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Strongly-typed mortgage request data (new format)
    /// </summary>
    public MortgageRequestDataDto RequestData { get; set; } = new();
    
    /// <summary>
    /// Legacy dictionary format for backward compatibility
    /// </summary>
    public Dictionary<string, object> RequestDataLegacy { get; set; } = new();
}

/// <summary>
/// Strongly-typed mortgage request data model (client-side)
/// </summary>
public class MortgageRequestDataDto
{
    public MortgageIncomeDataDto Income { get; set; } = new();
    public MortgageCreditDataDto Credit { get; set; } = new();
    public MortgageEmploymentDataDto Employment { get; set; } = new();
    public MortgagePropertyDataDto Property { get; set; } = new();
}

/// <summary>
/// Income verification data (client-side)
/// </summary>
public class MortgageIncomeDataDto
{
    public decimal? AnnualIncome { get; set; }
    public string EmploymentType { get; set; } = string.Empty;
    public decimal? YearsEmployed { get; set; }
}

/// <summary>
/// Credit report data (client-side)
/// </summary>
public class MortgageCreditDataDto
{
    public int? Score { get; set; }
    public DateTime? ReportDate { get; set; }
    public decimal? OutstandingDebts { get; set; }
}

/// <summary>
/// Employment verification data (client-side)
/// </summary>
public class MortgageEmploymentDataDto
{
    public string EmployerName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public decimal? MonthlySalary { get; set; }
    public bool IsVerified { get; set; }
}

/// <summary>
/// Property appraisal data (client-side)
/// </summary>
public class MortgagePropertyDataDto
{
    public decimal? PropertyValue { get; set; }
    public decimal? LoanAmount { get; set; }
    public string PropertyType { get; set; } = string.Empty;
    public DateTime? AppraisalDate { get; set; }
    public bool AppraisalCompleted { get; set; }
}

public class CrossServiceVerificationResultDto
{
    public DocumentVerificationResultDto DocumentVerification { get; set; } = new();
    public FinancialVerificationResultDto FinancialVerification { get; set; } = new();
    public bool AllVerificationsPassed { get; set; }
    public List<string> FailureReasons { get; set; } = new();
    public Dictionary<string, object> AdditionalData { get; set; } = new();
    public List<ServiceErrorDto> ServiceErrors { get; set; } = new();
}

public class ServiceErrorDto
{
    public string ServiceName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime ErrorTime { get; set; }
    public bool IsConnectivityError { get; set; }
}

public class DocumentVerificationResultDto
{
    public string UserName { get; set; } = string.Empty;
    public List<VerifiedDocumentDto> Documents { get; set; } = new();
    public bool HasIncomeDocuments { get; set; }
    public bool HasCreditReport { get; set; }
    public bool HasEmploymentVerification { get; set; }
    public bool HasPropertyAppraisal { get; set; }
    public bool AllDocumentsVerified { get; set; }
}

public class VerifiedDocumentDto
{
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class FinancialVerificationResultDto
{
    public string UserName { get; set; } = string.Empty;
    public bool AccountExists { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal AverageMonthlyIncome { get; set; }
    public bool HasSufficientFunds { get; set; }
    public bool IncomeConsistent { get; set; }
    public decimal DebtToIncomeRatio { get; set; }
}

public class VerificationStatusDto
{
    public Guid RequestId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string StatusReason { get; set; } = string.Empty;
    public DocumentVerificationSummaryDto DocumentVerification { get; set; } = new();
    public FinancialVerificationSummaryDto FinancialVerification { get; set; } = new();
    public CrossServiceVerificationSummaryDto CrossServiceVerification { get; set; } = new();
}

public class DocumentVerificationSummaryDto
{
    public bool AllDocumentsVerified { get; set; }
    public bool HasIncomeDocuments { get; set; }
    public bool HasCreditReport { get; set; }
    public bool HasEmploymentVerification { get; set; }
    public bool HasPropertyAppraisal { get; set; }
    public int DocumentCount { get; set; }
}

public class FinancialVerificationSummaryDto
{
    public bool AccountExists { get; set; }
    public decimal CurrentBalance { get; set; }
    public bool HasSufficientFunds { get; set; }
    public bool IncomeConsistent { get; set; }
}

public class CrossServiceVerificationSummaryDto
{
    public bool AllVerificationsPassed { get; set; }
    public List<string> FailureReasons { get; set; } = new();
    public DateTime VerificationDate { get; set; }
    public List<ServiceErrorDto> ServiceErrors { get; set; } = new();
    public bool HasServiceErrors { get; set; }
    public int ConnectivityErrors { get; set; }
}
