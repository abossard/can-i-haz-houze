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
            var mcpTools = await mcpClientService.ListToolsAsync(mcpEndpointUrl, cancellationToken);
            
            if (mcpTools.Count == 0)
            {
                logger.LogWarning("No tools found at MCP endpoint: {Endpoint}", mcpEndpointUrl);
                return;
            }
            
            logger.LogInformation("Found {Count} tools from MCP server", mcpTools.Count);
            
            // Convert each MCP tool to a Semantic Kernel function
            var plugin = new List<KernelFunction>();
            
            foreach (var mcpTool in mcpTools)
            {
                var kernelFunction = CreateKernelFunctionFromMcpTool(
                    mcpTool, 
                    mcpClientService, 
                    mcpEndpointUrl, 
                    logger);
                    
                plugin.Add(kernelFunction);
                
                logger.LogInformation("Registered MCP tool '{Tool}' as SK function", mcpTool.Name);
            }
            
            // Add all functions as a plugin
            builder.Plugins.AddFromFunctions(pluginName, plugin);
            
            logger.LogInformation("Successfully registered {Count} MCP tools as plugin '{Plugin}'", mcpTools.Count, pluginName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load MCP tools from {Endpoint}", mcpEndpointUrl);
            throw;
        }
    }
    
    private static KernelFunction CreateKernelFunctionFromMcpTool(
        McpClientTool mcpTool,
        IMcpClientService mcpClientService,
        string mcpEndpointUrl,
        ILogger logger)
    {
        // Create the function implementation that calls the MCP tool
        async Task<string> Implementation(Kernel kernel, KernelArguments arguments)
        {
            try
            {
                logger.LogInformation("Invoking MCP tool '{Tool}' via Semantic Kernel", mcpTool.Name);
                
                // Convert KernelArguments to dictionary for MCP
                var mcpArguments = new Dictionary<string, object>();
                foreach (var arg in arguments)
                {
                    mcpArguments[arg.Key] = arg.Value ?? string.Empty;
                }
                
                // Call the MCP tool
                var result = await mcpClientService.CallToolAsync(
                    mcpEndpointUrl,
                    mcpTool.Name,
                    mcpArguments,
                    CancellationToken.None);
                
                logger.LogInformation("MCP tool '{Tool}' executed successfully", mcpTool.Name);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing MCP tool '{Tool}'", mcpTool.Name);
                throw;
            }
        }
        
        // Build the function with metadata from MCP tool
        var functionBuilder = KernelFunctionFactory.CreateFromMethod(
            Implementation,
            functionName: mcpTool.Name,
            description: mcpTool.Description ?? $"MCP tool: {mcpTool.Name}");
        
        return functionBuilder;
    }
}
