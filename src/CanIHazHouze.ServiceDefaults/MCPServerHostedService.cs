using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Hosted service to manage the MCP server lifecycle
/// </summary>
public class MCPServerHostedService : IHostedService
{
    private readonly IMCPServer _mcpServer;
    private readonly MCPOptions _options;
    private readonly ILogger<MCPServerHostedService> _logger;

    public MCPServerHostedService(
        IMCPServer mcpServer, 
        IOptions<MCPOptions> options,
        ILogger<MCPServerHostedService> logger)
    {
        _mcpServer = mcpServer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MCP Server is disabled via configuration");
            return;
        }

        _logger.LogInformation("Starting MCP Server hosted service");
        await _mcpServer.StartAsync(cancellationToken);
        _logger.LogInformation("MCP Server hosted service started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _logger.LogInformation("Stopping MCP Server hosted service");
        await _mcpServer.StopAsync(cancellationToken);
        _logger.LogInformation("MCP Server hosted service stopped");
    }
}