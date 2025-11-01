using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CanIHazHouze.Tests;

/// <summary>
/// Tests for MCP (Model Context Protocol) server functionality
/// </summary>
public class MCPServerTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Category", "MCP")]
    public void MCPServer_ShouldRegisterAndExecuteTools()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MCPOptions>(options =>
        {
            options.Enabled = true;
            options.Endpoint = "/mcp";
        });
        services.AddSingleton<IMCPServer, AspireMCPServer>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mcpServer = serviceProvider.GetRequiredService<IMCPServer>();

        // Act - Register a test tool
        mcpServer.RegisterTool<TestRequest>("test_tool", 
            "A test tool for MCP validation",
            async req => new { message = $"Hello {req.Name}!", value = req.Value });

        // Assert - Verify tool is registered
        var tools = mcpServer.GetAvailableTools();
        Assert.Single(tools);
        Assert.Equal("test_tool", tools.First().Name);
        Assert.Equal("A test tool for MCP validation", tools.First().Description);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Category", "MCP")]
    public async Task MCPServer_ShouldExecuteToolCall()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MCPOptions>(options =>
        {
            options.Enabled = true;
            options.Endpoint = "/mcp";
        });
        services.AddSingleton<IMCPServer, AspireMCPServer>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mcpServer = serviceProvider.GetRequiredService<IMCPServer>();

        mcpServer.RegisterTool<TestRequest>("test_tool",
            "A test tool for MCP validation",
            async req => new { message = $"Hello {req.Name}!", value = req.Value });

        var testArgs = JsonDocument.Parse("""{"name": "MCP", "value": 42}""");

        // Act
        var result = await mcpServer.HandleToolCallAsync("test_tool", testArgs.RootElement);

        // Assert
        var resultJson = JsonSerializer.Serialize(result);
        Assert.Contains("Hello MCP!", resultJson);
        Assert.Contains("42", resultJson);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Category", "MCP")]
    public void MCPServer_ShouldRegisterResources()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MCPOptions>(options =>
        {
            options.Enabled = true;
            options.Endpoint = "/mcp";
        });
        services.AddSingleton<IMCPServer, AspireMCPServer>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mcpServer = serviceProvider.GetRequiredService<IMCPServer>();

        // Act
        mcpServer.RegisterResource("test://resource", "Test Resource", 
            "A test resource for MCP validation",
            async () => new { data = "test resource data" });

        // Assert
        var resources = mcpServer.GetAvailableResources();
        Assert.Single(resources);
        Assert.Equal("test://resource", resources.First().Uri);
        Assert.Equal("Test Resource", resources.First().Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Category", "MCP")]
    public async Task MCPServer_ShouldHandleResourceRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<MCPOptions>(options =>
        {
            options.Enabled = true;
            options.Endpoint = "/mcp";
        });
        services.AddSingleton<IMCPServer, AspireMCPServer>();
        
        var serviceProvider = services.BuildServiceProvider();
        var mcpServer = serviceProvider.GetRequiredService<IMCPServer>();

        mcpServer.RegisterResource("test://resource", "Test Resource",
            "A test resource for MCP validation",
            async () => new { data = "test resource data", timestamp = DateTimeOffset.UtcNow });

        // Act
        var result = await mcpServer.HandleResourceRequestAsync("test://resource");

        // Assert
        var resultJson = JsonSerializer.Serialize(result);
        Assert.Contains("test resource data", resultJson);
        Assert.Contains("timestamp", resultJson);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Category", "MCP")]
    public void MCPOptions_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var options = new MCPOptions();

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal("/mcp", options.Endpoint);
        Assert.Equal(100, options.MaxConnections);
        Assert.Equal(1024 * 1024, options.MessageSizeLimit);
        Assert.Single(options.AllowedOrigins);
        Assert.Equal("*", options.AllowedOrigins[0]);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ConnectionTimeout);
    }

    // Test request model for MCP tool testing
    public record TestRequest(string Name, int Value);
}