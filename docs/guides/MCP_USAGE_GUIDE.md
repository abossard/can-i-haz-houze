# MCP Server Usage Guide for CanIHazHouze

## Quick Start

The CanIHazHouze application now includes Model Context Protocol (MCP) server support, allowing AI assistants to interact directly with the mortgage approval system.

### Available MCP Endpoints

Each service exposes MCP capabilities at:
- **LedgerService**: `https://localhost:5001/mcp` 
- **DocumentService**: `https://localhost:5002/mcp`
- **MortgageApprover**: `https://localhost:5003/mcp`

### Discovering Available Tools

To see all available MCP tools and resources, visit the capabilities endpoint:

```bash
curl https://localhost:5001/mcp/capabilities
```

## Claude Desktop Configuration

To connect Claude Desktop to CanIHazHouze, add this to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "canihazhouze-ledger": {
      "command": "websocket",
      "args": ["ws://localhost:5001/mcp"],
      "env": {}
    },
    "canihazhouze-documents": {
      "command": "websocket", 
      "args": ["ws://localhost:5002/mcp"],
      "env": {}
    },
    "canihazhouze-mortgage": {
      "command": "websocket",
      "args": ["ws://localhost:5003/mcp"],
      "env": {}
    }
  }
}
```

## Available MCP Tools

### LedgerService Tools

#### `get_account_info`
Retrieve account information including balance and timestamps.

**Parameters:**
- `owner` (string): Username or identifier of the account owner

**Example:**
```json
{
  "name": "get_account_info",
  "arguments": {
    "owner": "john_doe"
  }
}
```

#### `update_account_balance`
Update account balance by adding or subtracting the specified amount.

**Parameters:**
- `owner` (string): Username or identifier of the account owner
- `amount` (number): Amount to add (positive) or subtract (negative)
- `description` (string): Description of the transaction

**Example:**
```json
{
  "name": "update_account_balance",
  "arguments": {
    "owner": "john_doe",
    "amount": 250.50,
    "description": "Salary deposit"
  }
}
```

#### `get_transaction_history`
Retrieve transaction history with pagination support.

**Parameters:**
- `owner` (string): Username or identifier of the account owner
- `skip` (number, optional): Number of transactions to skip (default: 0)
- `take` (number, optional): Maximum transactions to return (default: 50)

#### `reset_account`
Reset account to initial state with new random balance.

**Parameters:**
- `owner` (string): Username or identifier of the account owner

### DocumentService Tools

#### `upload_document`
Upload a new document with optional AI tag suggestions.

**Parameters:**
- `owner` (string): Username or identifier of the document owner
- `fileName` (string): Original filename of the document
- `base64Content` (string): Base64-encoded file content
- `tags` (array, optional): List of tags for document organization
- `suggestTags` (boolean, optional): Whether to generate AI tag suggestions
- `maxSuggestions` (number, optional): Maximum number of AI suggestions

#### `list_documents`
Get all documents for a user.

**Parameters:**
- `owner` (string): Username or identifier of the document owner

#### `get_document`
Get document metadata by ID.

**Parameters:**
- `id` (string): Unique GUID identifier of the document
- `owner` (string): Username or identifier of the document owner

#### `update_document_tags`
Update document tags.

**Parameters:**
- `id` (string): Unique GUID identifier of the document
- `owner` (string): Username or identifier of the document owner
- `tags` (array): Array of tag strings to assign to the document

#### `delete_document`
Delete a document.

**Parameters:**
- `id` (string): Unique GUID identifier of the document to delete
- `owner` (string): Username or identifier of the document owner

#### `verify_mortgage_documents`
Verify that a user has uploaded all required mortgage documents.

**Parameters:**
- `owner` (string): Username or identifier to verify documents for

#### `analyze_document_ai`
Analyze a document using AI to extract metadata and insights.

**Parameters:**
- `id` (string): Unique identifier of the document to analyze
- `owner` (string): Username or identifier of the document owner

### MortgageApprover Tools

#### `create_mortgage_request`
Create a new mortgage application request.

**Parameters:**
- `userName` (string): Username for mortgage application

#### `get_mortgage_request`
Retrieve mortgage application details by request ID.

**Parameters:**
- `requestId` (string): Unique GUID identifier of the mortgage request

#### `get_mortgage_request_by_user`
Retrieve mortgage application details by username.

**Parameters:**
- `userName` (string): Username to search for

#### `update_mortgage_data`
Update mortgage application data sections.

**Parameters:**
- `requestId` (string): Unique GUID identifier of the mortgage request
- `income` (object, optional): Income data to update
- `credit` (object, optional): Credit data to update
- `employment` (object, optional): Employment data to update
- `property` (object, optional): Property data to update

#### `verify_mortgage_request`
Trigger cross-service verification for mortgage request.

**Parameters:**
- `requestId` (string): Unique GUID identifier of the mortgage request

## AI Assistant Usage Examples

### Complete Mortgage Application Flow

```
User: "I want to apply for a mortgage for user john_doe"

AI: I'll help you start a mortgage application for john_doe. Let me create the request first.

[AI calls: create_mortgage_request(userName="john_doe")]

AI: Great! I've created mortgage request [ID]. Now let's check your account balance and verify you have the required documents.

[AI calls: get_account_info(owner="john_doe")]
[AI calls: verify_mortgage_documents(owner="john_doe")]

AI: I can see you have $2,500 in your account and you still need to upload some required documents. Let me help you understand what's needed...
```

### Document Analysis and Organization

```
User: "Analyze all my documents and suggest better tags"

AI: I'll review your documents and analyze them for better organization.

[AI calls: list_documents(owner="john_doe")]

AI: I found 3 documents. Let me analyze each one for better tagging:

[AI calls: analyze_document_ai(id="doc1", owner="john_doe")]
[AI calls: analyze_document_ai(id="doc2", owner="john_doe")]  
[AI calls: analyze_document_ai(id="doc3", owner="john_doe")]

AI: Based on my analysis, I recommend updating your document tags for better organization...
```

### Financial Account Management

```
User: "What's my current balance and recent transactions?"

AI: Let me check your account information and recent activity.

[AI calls: get_account_info(owner="john_doe")]
[AI calls: get_transaction_history(owner="john_doe", take=10)]

AI: Your current balance is $2,500.75. Here are your 10 most recent transactions...
```

## Configuration Options

### MCP Settings in appsettings.json

```json
{
  "MCP": {
    "Enabled": true,
    "Endpoint": "/mcp",
    "MaxConnections": 100,
    "MessageSizeLimit": 1048576,
    "AllowedOrigins": ["*"],
    "ConnectionTimeout": "00:30:00"
  }
}
```

### Environment Variables

```bash
MCP__Enabled=true
MCP__Endpoint=/mcp
MCP__MaxConnections=100
MCP__AllowedOrigins=https://claude.ai;https://localhost:3000
```

## Security Considerations

1. **Access Control**: All MCP tools respect the same user-based access control as REST APIs
2. **Rate Limiting**: Configure `MaxConnections` and `ConnectionTimeout` appropriately
3. **Origins**: Set `AllowedOrigins` to specific domains in production
4. **HTTPS**: Always use HTTPS/WSS in production environments

## Troubleshooting

### Connection Issues

1. **WebSocket Connection Failed**: Check that the service is running and MCP is enabled
2. **Tool Not Found**: Verify the tool name matches exactly (case-sensitive)
3. **Permission Denied**: Ensure the `owner` parameter matches your user context

### Debugging MCP

Enable detailed logging in appsettings.json:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.Extensions.Hosting.AspireMCPServer": "Debug",
      "Microsoft.Extensions.Hosting.MCPServerHostedService": "Debug"
    }
  }
}
```

### Testing MCP Tools

Use the built-in capabilities endpoint to test tool registration:

```bash
# Check available tools
curl https://localhost:5001/mcp/capabilities | jq '.capabilities.tools'

# Check available resources  
curl https://localhost:5001/mcp/capabilities | jq '.capabilities.resources'
```

## Performance Tips

1. **Batch Operations**: Use appropriate pagination parameters for large datasets
2. **Resource Management**: Close WebSocket connections when not in use
3. **Caching**: Results are not cached - consider caching on the client side
4. **Connection Pooling**: Reuse WebSocket connections when possible

## Next Steps

- Explore the full REST API documentation for additional context
- Test different tool combinations for complex workflows
- Set up monitoring for MCP endpoint usage
- Consider creating custom tools for specific business logic