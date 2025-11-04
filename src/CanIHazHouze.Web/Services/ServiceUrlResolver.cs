namespace CanIHazHouze.Web.Services;

/// <summary>
/// Resolves service URLs for client-side connections (e.g., SignalR)
/// </summary>
public interface IServiceUrlResolver
{
    string GetAgentServiceUrl();
}

public class ServiceUrlResolver : IServiceUrlResolver
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public ServiceUrlResolver(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public string GetAgentServiceUrl()
    {
        // Try to get from connection string (Aspire injects this)
        var connectionString = _configuration.GetConnectionString("agentservice");
        
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString.TrimEnd('/');
        }

        // Fallback: try to get from the HttpClient configuration
        // Create a temporary client to check its base address
        var client = _httpClientFactory.CreateClient("CanIHazHouze.Web.AgentApiClient");
        var baseAddress = client.BaseAddress?.ToString();
        
        if (!string.IsNullOrEmpty(baseAddress) && !baseAddress.Contains("https+http"))
        {
            return baseAddress.TrimEnd('/');
        }

        // Last resort: local development default
        return "https://localhost:7069";
    }
}
