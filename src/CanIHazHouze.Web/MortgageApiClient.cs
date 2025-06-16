using System.Text.Json;

namespace CanIHazHouze.Web;

public class MortgageApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MortgageApiClient> _logger;

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

    public async Task<MortgageRequestDto?> UpdateMortgageDataAsync(Guid requestId, Dictionary<string, object> data)
    {
        try
        {
            var request = new { Data = data };
            var response = await _httpClient.PutAsJsonAsync($"/mortgage-requests/{requestId}/data", request);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, GetJsonSerializerOptions());
            }
            
            _logger.LogWarning("Failed to update mortgage request {RequestId}. Status: {StatusCode}", requestId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating mortgage request {RequestId}", requestId);
            return null;
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
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }
}

public class MortgageRequestDto
{
    public Guid RequestId { get; set; }
    public Guid Id => RequestId; // Backward compatibility property
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusReason { get; set; } = string.Empty;
    public string MissingRequirements { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Dictionary<string, object> RequestData { get; set; } = new();
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
