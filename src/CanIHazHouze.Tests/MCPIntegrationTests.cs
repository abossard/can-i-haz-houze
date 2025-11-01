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

    /// <summary>
    /// Comprehensive smoke test that validates MCP is working across all four services
    /// This ensures the MCP implementation is functional end-to-end
    /// </summary>
    [Fact]
    public async Task MCP_SmokeTest_AllServices_ShouldExposeCapabilities()
    {
        // This is a smoke test that verifies MCP is properly configured and working
        // across all four services in the CanIHazHouze application
        
        var results = new List<(string Service, bool Success, int ToolCount)>();

        // Test 1: LedgerService MCP capabilities
        try
        {
            var ledgerFactory = new WebApplicationFactory<CanIHazHouze.LedgerService.Program>();
            var ledgerClient = ledgerFactory.CreateClient();
            var ledgerResponse = await ledgerClient.GetAsync("/mcp/capabilities");
            
            if (ledgerResponse.IsSuccessStatusCode)
            {
                var ledgerContent = await ledgerResponse.Content.ReadAsStringAsync();
                var ledgerCaps = JsonDocument.Parse(ledgerContent);
                var ledgerTools = ledgerCaps.RootElement.GetProperty("capabilities")
                    .GetProperty("tools").EnumerateArray().Count();
                results.Add(("LedgerService", true, ledgerTools));
            }
            else
            {
                results.Add(("LedgerService", false, 0));
            }
        }
        catch (Exception ex)
        {
            results.Add(("LedgerService", false, 0));
            throw new Exception($"LedgerService MCP test failed: {ex.Message}", ex);
        }

        // Test 2: DocumentService MCP capabilities
        try
        {
            var docFactory = new WebApplicationFactory<CanIHazHouze.DocumentService.Program>();
            var docClient = docFactory.CreateClient();
            var docResponse = await docClient.GetAsync("/mcp/capabilities");
            
            if (docResponse.IsSuccessStatusCode)
            {
                var docContent = await docResponse.Content.ReadAsStringAsync();
                var docCaps = JsonDocument.Parse(docContent);
                var docTools = docCaps.RootElement.GetProperty("capabilities")
                    .GetProperty("tools").EnumerateArray().Count();
                results.Add(("DocumentService", true, docTools));
            }
            else
            {
                results.Add(("DocumentService", false, 0));
            }
        }
        catch (Exception ex)
        {
            results.Add(("DocumentService", false, 0));
            throw new Exception($"DocumentService MCP test failed: {ex.Message}", ex);
        }

        // Test 3: MortgageApprover MCP capabilities
        try
        {
            var mortgageFactory = new WebApplicationFactory<CanIHazHouze.MortgageApprover.Program>();
            var mortgageClient = mortgageFactory.CreateClient();
            var mortgageResponse = await mortgageClient.GetAsync("/mcp/capabilities");
            
            if (mortgageResponse.IsSuccessStatusCode)
            {
                var mortgageContent = await mortgageResponse.Content.ReadAsStringAsync();
                var mortgageCaps = JsonDocument.Parse(mortgageContent);
                var mortgageTools = mortgageCaps.RootElement.GetProperty("capabilities")
                    .GetProperty("tools").EnumerateArray().Count();
                results.Add(("MortgageApprover", true, mortgageTools));
            }
            else
            {
                results.Add(("MortgageApprover", false, 0));
            }
        }
        catch (Exception ex)
        {
            results.Add(("MortgageApprover", false, 0));
            throw new Exception($"MortgageApprover MCP test failed: {ex.Message}", ex);
        }

        // Test 4: CrmService MCP capabilities
        try
        {
            var crmFactory = new WebApplicationFactory<CanIHazHouze.CrmService.Program>();
            var crmClient = crmFactory.CreateClient();
            var crmResponse = await crmClient.GetAsync("/mcp/capabilities");
            
            if (crmResponse.IsSuccessStatusCode)
            {
                var crmContent = await crmResponse.Content.ReadAsStringAsync();
                var crmCaps = JsonDocument.Parse(crmContent);
                var crmTools = crmCaps.RootElement.GetProperty("capabilities")
                    .GetProperty("tools").EnumerateArray().Count();
                results.Add(("CrmService", true, crmTools));
            }
            else
            {
                results.Add(("CrmService", false, 0));
            }
        }
        catch (Exception ex)
        {
            results.Add(("CrmService", false, 0));
            throw new Exception($"CrmService MCP test failed: {ex.Message}", ex);
        }

        // Assert all services are working
        Assert.All(results, result => 
        {
            Assert.True(result.Success, 
                $"MCP capabilities endpoint for {result.Service} failed");
            Assert.True(result.ToolCount > 0, 
                $"{result.Service} should expose at least 1 MCP tool, found {result.ToolCount}");
        });

        // Verify expected tool counts (as documented)
        var ledgerResult = results.First(r => r.Service == "LedgerService");
        var docResult = results.First(r => r.Service == "DocumentService");
        var mortgageResult = results.First(r => r.Service == "MortgageApprover");
        var crmResult = results.First(r => r.Service == "CrmService");

        Assert.Equal(4, ledgerResult.ToolCount); // 4 ledger tools
        Assert.Equal(7, docResult.ToolCount); // 7 document tools
        Assert.Equal(5, mortgageResult.ToolCount); // 5 mortgage tools
        Assert.Equal(8, crmResult.ToolCount); // 8 CRM tools

        // Calculate total tools across all services
        var totalTools = results.Sum(r => r.ToolCount);
        Assert.Equal(24, totalTools); // Total: 4 + 7 + 5 + 8 = 24 tools

        // Output results for visibility
        Console.WriteLine("MCP Smoke Test Results:");
        Console.WriteLine("======================");
        foreach (var result in results)
        {
            Console.WriteLine($"{result.Service}: {(result.Success ? "✓" : "✗")} ({result.ToolCount} tools)");
        }
        Console.WriteLine($"Total MCP Tools: {totalTools}");
    }
}