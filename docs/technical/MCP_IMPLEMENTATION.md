# MCP (Model Context Protocol) Implementation for CanIHazHouze

## Overview

This document outlines the implementation of Model Context Protocol (MCP) server support for the CanIHazHouze mortgage approval application. MCP enables AI assistants to securely connect to and interact with the application's APIs through a standardized protocol.

## What is MCP?

Model Context Protocol (MCP) is an open standard developed by Anthropic that enables AI assistants to securely connect to external data sources and tools. It provides:

- **Standardized Communication**: Uniform protocol for AI-to-service communication
- **Security**: Controlled access to external resources
- **Tool Integration**: AI assistants can call functions and access data
- **Resource Management**: Structured access to external resources

## Architecture Overview

### Current REST API Structure

The CanIHazHouze application exposes four main services:

1. **LedgerService**: Financial account and transaction management
2. **DocumentService**: Document upload, storage, and AI-powered analysis
3. **MortgageApprover**: Mortgage request processing and approval workflows
4. **CrmService**: Customer complaint management and support workflows

### MCP Integration Approach

The implementation will:
- Maintain existing REST APIs unchanged
- Add parallel MCP server endpoints
- Use Aspire Service Defaults for configuration
- Provide unified access to all four services through MCP

## MCP Server Components

### 1. Tools (Functions AI Can Call)

#### LedgerService Tools
- `get_account_info`: Retrieve account balance and details
- `update_account_balance`: Add/subtract money from account
- `get_transaction_history`: Retrieve transaction records
- `reset_account`: Reset account to initial state

#### DocumentService Tools  
- `upload_document`: Upload new documents
- `list_documents`: Get user's document list
- `get_document`: Retrieve document metadata
- `update_document_tags`: Modify document tags
- `delete_document`: Remove documents
- `download_document`: Get document content
- `verify_mortgage_documents`: Check required documents
- `analyze_document_ai`: AI-powered document analysis
- `enhance_document_tags`: AI tag suggestions

#### MortgageApprover Tools
- `create_mortgage_request`: Start new mortgage application
- `get_mortgage_request`: Retrieve application details
- `update_mortgage_data`: Modify application data
- `verify_mortgage_request`: Cross-service verification
- `get_verification_status`: Detailed verification info

#### CrmService Tools
- `create_complaint`: Create new customer complaint
- `get_complaints`: Retrieve all complaints for a customer
- `get_recent_complaints`: Get recent complaints across all customers
- `get_complaint`: Retrieve specific complaint by ID
- `update_complaint_status`: Update complaint status
- `add_complaint_comment`: Add support comment to complaint
- `add_complaint_approval`: Add approval decision to complaint
- `delete_complaint`: Delete complaint and associated data

### 2. Resources (Data AI Can Access)

#### LedgerService Resources
- Account summaries and balances
- Transaction histories
- Financial metrics

#### DocumentService Resources
- Document catalogs and metadata
- Document verification status
- AI analysis results

#### MortgageApprover Resources
- Mortgage application statuses
- Approval criteria and requirements
- Cross-service verification results

#### CrmService Resources
- Complaint summaries and metadata
- Complaint status tracking
- Customer support history

## Technical Implementation

### 1. MCP Server Library

```csharp
// Core MCP server infrastructure
public interface IMCPServer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    void RegisterTool<T>(string name, Func<T, Task<object>> handler);
    void RegisterResource(string uri, Func<Task<object>> provider);
}

public class AspireMCPServer : IMCPServer
{
    // Implementation using Aspire service defaults
    // WebSocket-based communication
    // JSON-RPC protocol handling
}
```

### 2. Service Integration

Each service will register its MCP tools and resources:

```csharp
// In LedgerService Program.cs
builder.Services.AddMCPServer(mcp =>
{
    mcp.RegisterTool<GetAccountRequest>("get_account_info", 
        async req => await ledgerService.GetAccountAsync(req.Owner));
    
    mcp.RegisterTool<UpdateBalanceRequest>("update_account_balance",
        async req => await ledgerService.UpdateBalanceAsync(req.Owner, req.Amount, req.Description));
    
    // Additional tools...
});
```

### 3. Aspire Service Defaults Integration

```csharp
// In ServiceDefaults/Extensions.cs
public static TBuilder AddMCPSupport<TBuilder>(this TBuilder builder) 
    where TBuilder : IHostApplicationBuilder
{
    // Add MCP server services
    builder.Services.AddSingleton<IMCPServer, AspireMCPServer>();
    
    // Configure MCP endpoints
    builder.Services.Configure<MCPOptions>(options =>
    {
        options.EnableMCP = true;
        options.MCPEndpoint = "/mcp";
        options.AllowedOrigins = ["*"]; // Configure for production
    });
    
    return builder;
}

public static WebApplication MapMCPEndpoints(this WebApplication app)
{
    var mcpServer = app.Services.GetRequiredService<IMCPServer>();
    
    // Map WebSocket endpoint for MCP
    app.MapWebSocket("/mcp", async (WebSocket webSocket) =>
    {
        await mcpServer.HandleConnectionAsync(webSocket);
    });
    
    return app;
}
```

## Protocol Details

### Message Format

MCP uses JSON-RPC 2.0 over WebSocket:

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "get_account_info",
    "arguments": {
      "owner": "john_doe"
    }
  },
  "id": 1
}
```

### Tool Schema Definition

```json
{
  "name": "get_account_info",
  "description": "Retrieve account information including balance and timestamps",
  "inputSchema": {
    "type": "object",
    "properties": {
      "owner": {
        "type": "string",
        "description": "Username or identifier of the account owner"
      }
    },
    "required": ["owner"]
  }
}
```

## Security Considerations

### Authentication & Authorization
- User-based access control for all operations
- Service-to-service authentication via Aspire
- API key validation for external MCP clients

### Data Protection
- No sensitive data in tool schemas
- Parameterized queries to prevent injection
- Rate limiting on MCP endpoints

### Network Security
- HTTPS/WSS only in production
- CORS configuration for allowed origins
- Request size limits

## Configuration

### appsettings.json
```json
{
  "MCP": {
    "Enabled": true,
    "Endpoint": "/mcp",
    "MaxConnections": 100,
    "MessageSizeLimit": "1MB",
    "AllowedOrigins": ["https://claude.ai", "https://localhost:*"]
  }
}
```

### Environment Variables
```bash
MCP__Enabled=true
MCP__Endpoint=/mcp
MCP__AllowedOrigins=https://claude.ai;https://localhost:3000
```

## Usage Examples

### Claude Desktop Configuration

```json
{
  "mcpServers": {
    "canihazhouze": {
      "command": "websocket",
      "args": ["ws://localhost:5000/mcp"]
    }
  }
}
```

### AI Assistant Interactions

```
User: "Check my account balance for user john_doe"

AI calls: get_account_info(owner="john_doe")
Response: {"owner": "john_doe", "balance": 2500.75, "createdAt": "...", "lastUpdatedAt": "..."}

AI: "Your account balance is $2,500.75. The account was created on [date] and last updated on [date]."
```

## Deployment Strategy

### Development
1. Enable MCP in local development environments
2. Test with Claude Desktop or MCP client tools
3. Validate all tools and resources

### Production
1. Configure authentication and security
2. Set up monitoring for MCP connections
3. Implement rate limiting and abuse protection

## Benefits

### For Users
- Natural language interaction with mortgage system
- AI-assisted document processing and verification
- Automated financial analysis and reporting

### For Developers  
- Standardized protocol reduces integration complexity
- Maintains existing REST API investments
- Enables rich AI-powered user experiences

### For AI Assistants
- Direct access to real-time financial data
- Document processing capabilities
- Mortgage workflow automation

## Future Enhancements

1. **Streaming Support**: Real-time updates for long-running operations
2. **Batch Operations**: Multiple tool calls in single request
3. **Advanced Analytics**: AI-powered insights and recommendations
4. **Integration Hub**: Connect with external mortgage and financial services

## Conclusion

The MCP implementation will transform CanIHazHouze into an AI-native mortgage platform while preserving all existing functionality. This approach provides a solid foundation for future AI-powered features and integrations.