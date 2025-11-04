# MCP Overhaul Implementation Summary

## Overview
Replaced custom WebSocket-based MCP implementation with official Model Context Protocol C# SDK (HTTP/SSE transport).

## Changes Made

### 1. ServiceDefaults Package & Extensions

**File: `src/CanIHazHouze.ServiceDefaults/CanIHazHouze.ServiceDefaults.csproj`**
- ✅ Added `ModelContextProtocol` package (0.4.0-preview.3)
- ✅ Added `ModelContextProtocol.AspNetCore` package (0.4.0-preview.3)

**File: `src/CanIHazHouze.ServiceDefaults/Extensions.cs`**
- ✅ Replaced custom WebSocket MCP with official SDK
- ✅ Updated imports: Added `ModelContextProtocol.AspNetCore`, `ModelContextProtocol.Server`
- ✅ Simplified `AddMCPSupport()`: Now uses `builder.Services.AddMcp(options => { options.ServerInfo = new ServerInfo {...} })`
- ✅ Updated `MapDefaultEndpoints()`: Now uses `app.MapMcp("/mcp")` (HTTP/SSE endpoint)
- ✅ Removed all custom code: HandleMCPWebSocketConnection, ProcessMCPMessage, HandleToolCall, HandleResourceRead

**Files Removed:**
- ✅ `src/CanIHazHouze.ServiceDefaults/MCPServer.cs` (custom implementation)
- ✅ `src/CanIHazHouze.ServiceDefaults/MCPServerHostedService.cs` (not needed with official SDK)
- ✅ `src/CanIHazHouze.ServiceDefaults/MCPOptions.cs` (replaced by official `McpServerOptions`)

### 2. Migration Strategy for Services

**Required Changes for Each Service (LedgerService, DocumentService, CRMService, MortgageApprover):**

#### Approach A: Attribute-Based (Recommended by Official SDK)
Create a new class with `[McpServerToolType]` attribute and methods with `[McpServerTool]` attribute:

```csharp
// Example for LedgerService
[McpServerToolType]
public class LedgerMcpTools
{
    private readonly ILedgerService _ledgerService;
    
    public LedgerMcpTools(ILedgerService ledgerService) 
    {
        _ledgerService = ledgerService;
    }
    
    [McpServerTool, Description("Get account information for a user")]
    public async Task<AccountInfo> GetAccountInfo(
        [Description("Username or identifier of the account owner")] string owner)
    {
        return await _ledgerService.GetAccountAsync(owner);
    }
    
    // ... more tools
}

// In Program.cs:
builder.AddServiceDefaults()
    .AddMCPSupport()
    .WithTools<LedgerMcpTools>();
```

#### Approach B: Programmatic (Maintains Existing Pattern)
Use `McpServerTool.Create()` with existing async lambda handlers:

```csharp
// In Program.cs after MapDefaultEndpoints():
var mcpBuilder = builder.AddServiceDefaults().AddMCPSupport();

mcpBuilder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    return McpServerTool.Create(
        async (GetAccountRequest req) => 
        {
            using var scope = sp.CreateScope();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ILedgerService>();
            return await ledgerService.GetAccountAsync(req.Owner);
        },
        new McpServerToolCreateOptions 
        {
            Name = "get_account_info",
            Description = "Get account information for a user",
            Services = sp
        });
});
```

### 3. Current Service Status

**Services with MCP Registration (Need Migration):**

1. **LedgerService** (`src/CanIHazHouze.LedgerService/Program.cs`):
   - **Status**: ⏳ Pending migration
   - Request models defined: `GetAccountRequest`, `UpdateBalanceRequest`, `GetTransactionsRequest`, `ResetAccountRequest`
   - Note: No actual MCP registration code found in current file (may have been removed previously)

2. **DocumentService** (`src/CanIHazHouze.DocumentService/Program.cs`):
   - **Status**: ⏳ Needs migration
   - Current registration: Lines 1213-1320+ use `mcpServer.RegisterTool<T>()`
   - Tools: upload_document, list_documents, get_document, update_document_tags, delete_document, verify_mortgage_documents, analyze_document_ai
   - Request models: `UploadDocumentMCPRequest`, `ListDocumentsRequest`, etc.

3. **CRMService** (`src/CanIHazHouze.CrmService/Program.cs`):
   - **Status**: ⏳ Needs migration
   - Current registration: Lines 510-594 use `mcpServer.RegisterTool<T>()`
   - Tools: create_complaint, get_complaints, get_recent_complaints, get_complaint, update_complaint_status, add_complaint_comment, add_complaint_approval, delete_complaint
   - Request models: `CreateComplaintMcpRequest`, `GetComplaintsMcpRequest`, etc.

4. **MortgageApprover** (`src/CanIHazHouze.MortgageApprover/Program.cs`):
   - **Status**: ⏳ Needs migration
   - Current registration: Lines 398-460+ use `mcpServer.RegisterTool<T>()`
   - Tools: create_mortgage_request, get_mortgage_request, get_mortgage_request_by_user, update_mortgage_data, verify_mortgage_request
   - Request models: `CreateMortgageRequestMCPRequest`, `GetMortgageRequestRequest`, etc.

### 4. Client-Side (Already Complete)

**AgentService**:
- ✅ Already uses official MCP Client SDK (`ModelContextProtocol 0.4.0-preview.3`)
- ✅ `McpClientService` with `HttpClientTransport`
- ✅ `McpToSemanticKernelConverter` for dynamic tool discovery
- ✅ Aspire service discovery integration (`https+http://servicename/mcp`)

## Next Steps

1. **Choose Migration Approach**:
   - **Recommendation**: Use Approach A (Attribute-Based) for cleaner, more maintainable code
   - Fallback: Use Approach B (Programmatic) if minimal code changes are required

2. **Migrate Each Service** (in order of dependency):
   a. LedgerService (no dependencies)
   b. DocumentService (no dependencies)
   c. CRMService (no dependencies)
   d. MortgageApprover (depends on others)

3. **Test Each Service**:
   - Run AppHost
   - Verify MCP endpoint responds at `http://servicename/mcp`
   - Test tool discovery with AgentService
   - Execute agent: "Look how much money Andre has"

4. **Verify Complete Integration**:
   - Agent successfully discovers tools from all services
   - Agent can call tools via MCP
   - Logs show tool invocations and results

## Technical Notes

- **Transport**: Changed from WebSockets to HTTP/SSE (official MCP standard)
- **Endpoint**: `/mcp` for all services (consistent)
- **Schema**: Official SDK automatically generates JSON schemas from C# types/attributes
- **Error Handling**: Official SDK provides built-in JSON-RPC error responses
- **Service Discovery**: Aspire automatically resolves `https+http://servicename/mcp` to `http://servicename/mcp`
- **Compatibility**: Client (AgentService) and Server (all services) now use same protocol version

## References

- Official MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- Documentation: https://learn.microsoft.com/en-us/dotnet/ai/get-started-mcp
- Tutorial: https://learn.microsoft.com/en-us/azure/app-service/tutorial-ai-model-context-protocol-server-dotnet
