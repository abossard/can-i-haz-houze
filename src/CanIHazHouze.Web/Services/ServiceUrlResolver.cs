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
        // Aspire injects service URLs as services:servicename:https:0 (or :http:0)
        // In Azure Container Apps, these come as environment variables: services__servicename__https__0
        var httpsUrl = _configuration["services:agentservice:https:0"];
        var httpUrl = _configuration["services:agentservice:http:0"];
        
        var serviceUrl = httpsUrl ?? httpUrl;
        
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            return serviceUrl.TrimEnd('/');
        }

        // Fallback: try to get the base URL from the configured HttpClient
        // This works because the HttpClient uses service discovery
        var client = _httpClientFactory.CreateClient("CanIHazHouze.Web.AgentApiClient");
        var baseAddress = client.BaseAddress?.ToString();
        
        if (!string.IsNullOrEmpty(baseAddress) && !baseAddress.Contains("https+http"))
        {
            return baseAddress.TrimEnd('/');
        }

        // Last resort: local development default (shouldn't be reached if AppHost is running)
        return "https://localhost:7069";
    }
}
