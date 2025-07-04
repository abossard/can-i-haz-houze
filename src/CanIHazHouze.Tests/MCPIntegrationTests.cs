using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CanIHazHouze.Tests;

/// <summary>
/// Integration tests for MCP endpoints across all services
/// </summary>
public class MCPIntegrationTests
{
    [Fact]
    public async Task LedgerService_MCPCapabilities_ShouldReturnTools()
    {
        // This test validates that the LedgerService properly exposes MCP capabilities
        var factory = new WebApplicationFactory<CanIHazHouze.LedgerService.Program>();
        
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp/capabilities");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonDocument.Parse(content);
        
        var tools = capabilities.RootElement.GetProperty("capabilities")
            .GetProperty("tools").EnumerateArray().ToList();
        
        // Should have at least the ledger tools
        Assert.True(tools.Count >= 4);
        
        var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("get_account_info", toolNames);
        Assert.Contains("update_account_balance", toolNames);
        Assert.Contains("get_transaction_history", toolNames);
        Assert.Contains("reset_account", toolNames);
    }

    [Fact]
    public async Task DocumentService_MCPCapabilities_ShouldReturnTools()
    {
        // This test validates that the DocumentService properly exposes MCP capabilities
        var factory = new WebApplicationFactory<CanIHazHouze.DocumentService.Program>();
        
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp/capabilities");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonDocument.Parse(content);
        
        var tools = capabilities.RootElement.GetProperty("capabilities")
            .GetProperty("tools").EnumerateArray().ToList();
        
        // Should have at least the document tools
        Assert.True(tools.Count >= 7);
        
        var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("upload_document", toolNames);
        Assert.Contains("list_documents", toolNames);
        Assert.Contains("get_document", toolNames);
        Assert.Contains("update_document_tags", toolNames);
        Assert.Contains("delete_document", toolNames);
        Assert.Contains("verify_mortgage_documents", toolNames);
        Assert.Contains("analyze_document_ai", toolNames);
    }

    [Fact]
    public async Task MortgageApprover_MCPCapabilities_ShouldReturnTools()
    {
        // This test validates that the MortgageApprover properly exposes MCP capabilities
        var factory = new WebApplicationFactory<CanIHazHouze.MortgageApprover.Program>();
        
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp/capabilities");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonDocument.Parse(content);
        
        var tools = capabilities.RootElement.GetProperty("capabilities")
            .GetProperty("tools").EnumerateArray().ToList();
        
        // Should have at least the mortgage tools
        Assert.True(tools.Count >= 5);
        
        var toolNames = tools.Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("create_mortgage_request", toolNames);
        Assert.Contains("get_mortgage_request", toolNames);
        Assert.Contains("get_mortgage_request_by_user", toolNames);
        Assert.Contains("update_mortgage_data", toolNames);
        Assert.Contains("verify_mortgage_request", toolNames);
    }

    [Fact]
    public async Task MCPCapabilities_ShouldIncludeProtocolVersion()
    {
        var factory = new WebApplicationFactory<CanIHazHouze.LedgerService.Program>();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp/capabilities");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonDocument.Parse(content);
        
        Assert.True(capabilities.RootElement.TryGetProperty("protocolVersion", out var version));
        Assert.Equal("2024-11-05", version.GetString());
        
        Assert.True(capabilities.RootElement.TryGetProperty("serverInfo", out var serverInfo));
        Assert.True(serverInfo.TryGetProperty("name", out var name));
        Assert.Equal("CanIHazHouze MCP Server", name.GetString());
    }

    [Fact]
    public async Task MCPCapabilities_ShouldIncludeResources()
    {
        var factory = new WebApplicationFactory<CanIHazHouze.LedgerService.Program>();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/mcp/capabilities");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var capabilities = JsonDocument.Parse(content);
        
        var resources = capabilities.RootElement.GetProperty("capabilities")
            .GetProperty("resources").EnumerateArray().ToList();
        
        // Should have at least one resource
        Assert.True(resources.Count >= 1);
        
        var resourceUris = resources.Select(r => r.GetProperty("uri").GetString()).ToList();
        Assert.Contains("ledger://accounts/summary", resourceUris);
    }

    [Fact]
    public void MCPConfiguration_ShouldBeEnabledByDefault()
    {
        var factory = new WebApplicationFactory<CanIHazHouze.LedgerService.Program>();
        
        using var scope = factory.Services.CreateScope();
        var mcpServer = scope.ServiceProvider.GetService<IMCPServer>();
        
        // MCP server should be registered
        Assert.NotNull(mcpServer);
        
        // Should have tools registered
        var tools = mcpServer.GetAvailableTools();
        Assert.True(tools.Any());
    }

    [Fact]
    public async Task RESTAPIs_ShouldStillWork_WithMCPEnabled()
    {
        // Ensure MCP doesn't break existing REST functionality
        var factory = new WebApplicationFactory<CanIHazHouze.LedgerService.Program>();
        var client = factory.CreateClient();

        // Test existing REST endpoint still works
        var response = await client.GetAsync("/accounts/test-user");
        
        // Should either return account info or create new account (both are success cases)
        Assert.True(response.IsSuccessStatusCode);
    }
}