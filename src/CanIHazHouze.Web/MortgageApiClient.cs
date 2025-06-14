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
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            
            _logger.LogWarning("Failed to create mortgage request for user {UserName}. Status: {StatusCode}", userName, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating mortgage request for user {UserName}", userName);
            return null;
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
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
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
                return JsonSerializer.Deserialize<MortgageRequestDto>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
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
                return JsonSerializer.Deserialize<List<MortgageRequestDto>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<MortgageRequestDto>();
            }
            
            _logger.LogWarning("Failed to get mortgage requests. Status: {StatusCode}", response.StatusCode);
            return new List<MortgageRequestDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mortgage requests");
            return new List<MortgageRequestDto>();
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
}

public class MortgageRequestDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusReason { get; set; } = string.Empty;
    public string MissingRequirements { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Dictionary<string, object> RequestData { get; set; } = new();
}
