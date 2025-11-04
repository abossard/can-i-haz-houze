using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace CanIHazHouze.AgentService.Services;

/// <summary>
/// Helper to convert MCP tools to Semantic Kernel functions
/// </summary>
public static class McpToSemanticKernelConverter
{
    /// <summary>
    /// Converts MCP tools to Semantic Kernel functions and adds them to the kernel
    /// Uses the built-in AsKernelFunction() extension method available in SK 1.44+
    /// </summary>
    public static async Task AddMcpToolsAsync(
        this IKernelBuilder builder,
        IMcpClientService mcpClientService,
        string mcpEndpointUrl,
        string pluginName,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Loading MCP tools from {Endpoint} as plugin '{Plugin}'", mcpEndpointUrl, pluginName);
            
            // List all tools from the MCP server
            // McpClientTool implements AIFunction, so it can be directly converted to KernelFunction
            var mcpTools = await mcpClientService.ListToolsAsync(mcpEndpointUrl, cancellationToken);
            
            if (mcpTools.Count == 0)
            {
                logger.LogWarning("No tools found at MCP endpoint: {Endpoint}", mcpEndpointUrl);
                return;
            }
            
            logger.LogInformation("Found {Count} tools from MCP server: {Tools}", 
                mcpTools.Count, 
                string.Join(", ", mcpTools.Select(t => t.Name)));
            
            // Convert MCP tools to Kernel functions using the built-in AsKernelFunction() extension
            // This properly preserves all parameter metadata from the MCP tool's InputSchema
            var kernelFunctions = mcpTools.Select(tool => tool.AsKernelFunction()).ToList();
            
            // Log function metadata for debugging
            foreach (var function in kernelFunctions)
            {
                var paramInfo = function.Metadata.Parameters.Any() 
                    ? string.Join(", ", function.Metadata.Parameters.Select(p => $"{p.Name} ({p.ParameterType?.Name ?? "string"}, Required: {p.IsRequired})"))
                    : "No parameters";
                
                logger.LogInformation("Registered MCP tool: {Tool} - {Description}. Parameters: {Parameters}", 
                    function.Name, 
                    function.Description ?? "No description",
                    paramInfo);
            }
            
            // Add all functions as a plugin
            builder.Plugins.AddFromFunctions(pluginName, kernelFunctions);
            
            logger.LogInformation("Successfully registered {Count} MCP tools as plugin '{Plugin}'", mcpTools.Count, pluginName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load MCP tools from {Endpoint}", mcpEndpointUrl);
            throw;
        }
    }

}
