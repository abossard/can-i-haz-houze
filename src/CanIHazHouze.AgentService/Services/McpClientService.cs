using ModelContextProtocol.Client;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CanIHazHouze.AgentService.Services;

/// <summary>
/// Service for connecting to MCP servers and invoking their tools
/// </summary>
public interface IMcpClientService
{
    /// <summary>
    /// Lists all available tools from an MCP server
    /// </summary>
    Task<IList<McpClientTool>> ListToolsAsync(string mcpEndpointUrl, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Calls a specific tool on an MCP server
    /// </summary>
    Task<string> CallToolAsync(string mcpEndpointUrl, string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
}

public class McpClientService : IMcpClientService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpClientService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, McpClient> _clientCache = new();

    public McpClientService(IHttpClientFactory httpClientFactory, ILogger<McpClientService> logger, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    private async Task<McpClient> GetOrCreateClientAsync(string mcpEndpointUrl, CancellationToken cancellationToken)
    {
        if (_clientCache.TryGetValue(mcpEndpointUrl, out var cachedClient))
        {
            return cachedClient;
        }

        _logger.LogInformation("Creating MCP client for endpoint: {Endpoint}", mcpEndpointUrl);
        
        // Create HTTP client configured with Aspire service discovery
        // The HttpClientFactory will handle Aspire's service discovery format (https+http://)
        var httpClient = _httpClientFactory.CreateClient();
        
        // For Aspire service discovery URLs, we need to make an initial request to get the actual URL
        // But for MCP, we'll use the URI directly and let HttpClient resolve it
        Uri endpoint;
        if (mcpEndpointUrl.StartsWith("https+http://", StringComparison.OrdinalIgnoreCase))
        {
            // Convert Aspire service discovery format to standard HTTPS for the transport
            // The actual resolution will happen via the HttpClient
            var servicePath = mcpEndpointUrl.Substring("https+http://".Length);
            endpoint = new Uri($"http://{servicePath}");
        }
        else
        {
            endpoint = new Uri(mcpEndpointUrl);
        }
        
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.AutoDetect // Try Streamable HTTP first, fall back to SSE
            },
            httpClient,
            _loggerFactory
        );

        var client = await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
        _clientCache[mcpEndpointUrl] = client;
        
        _logger.LogInformation("MCP client created successfully for: {Endpoint}", mcpEndpointUrl);
        return client;
    }

    public async Task<IList<McpClientTool>> ListToolsAsync(string mcpEndpointUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetOrCreateClientAsync(mcpEndpointUrl, cancellationToken);
            // ListToolsAsync() in 0.4.0-preview.3 doesn't take cancellationToken
            var tools = await client.ListToolsAsync();
            
            _logger.LogInformation("Retrieved {Count} tools from MCP server: {Endpoint}", tools.Count, mcpEndpointUrl);
            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing tools from MCP server: {Endpoint}", mcpEndpointUrl);
            throw;
        }
    }

    public async Task<string> CallToolAsync(
        string mcpEndpointUrl, 
        string toolName, 
        Dictionary<string, object> arguments, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetOrCreateClientAsync(mcpEndpointUrl, cancellationToken);
            
            _logger.LogInformation("Calling MCP tool '{Tool}' at {Endpoint}", toolName, mcpEndpointUrl);
            
            // Convert to IReadOnlyDictionary with nullable values for API compatibility
            var readOnlyArgs = arguments.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
            var result = await client.CallToolAsync(toolName, readOnlyArgs, cancellationToken: cancellationToken);
            
            // Convert result to string - MCP returns ContentBlock list
            var resultText = string.Join("\n", result.Content.Select(c => c.Type == "text" ? c.ToString() : JsonSerializer.Serialize(c)));
            
            _logger.LogInformation("MCP tool '{Tool}' returned result", toolName);
            return resultText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling MCP tool '{Tool}' at {Endpoint}", toolName, mcpEndpointUrl);
            throw;
        }
    }
}
