# MCP Server Configuration for GitHub Copilot Coding Agent

This repository is configured to use Model Context Protocol (MCP) servers that extend GitHub Copilot coding agent's capabilities.

## Available MCP Servers

### 1. Docker CLI
- **Command**: `docker`
- **Purpose**: Allows Copilot to run Docker commands for container management
- **Use Cases**:
  - Build Docker images
  - Run containers
  - Inspect container logs
  - Manage Docker networks and volumes

### 2. Docker Compose
- **Command**: `docker-compose`
- **Purpose**: Enables Copilot to orchestrate multi-container applications
- **Use Cases**:
  - Start/stop development services (Cosmos DB, Azurite)
  - View service logs
  - Manage container dependencies

### 3. Web Search (Brave Search)
- **Command**: `npx -y @modelcontextprotocol/server-brave-search`
- **Purpose**: Provides web search capabilities for finding documentation, examples, and solutions
- **Use Cases**:
  - Search for API documentation
  - Find code examples
  - Look up error messages
  - Research best practices

## Setup Instructions

### For Repository Administrators

1. **Configure MCP in GitHub Repository Settings**:
   - Go to Repository Settings → Code & automation → Copilot → Coding agent
   - In the MCP configuration section, paste the contents of `.github/copilot-mcp.json`

2. **Add Required Secrets**:
   - Go to Repository Settings → Secrets and variables → Codespaces (or Actions)
   - Add the following secret:
     - `COPILOT_MCP_BRAVE_API_KEY`: Your Brave Search API key
       - Get a free API key at: https://brave.com/search/api/

### For Developers (Local Development)

To use MCP servers in VS Code with Copilot:

1. **Install Prerequisites**:
   ```bash
   # Docker Desktop (required)
   # Download from: https://www.docker.com/products/docker-desktop
   
   # Node.js (for web search MCP server)
   # Download from: https://nodejs.org/
   ```

2. **Create VS Code MCP Configuration**:
   Create or update `.vscode/mcp.json` in your workspace:
   ```json
   {
     "mcpServers": {
       "docker": {
         "command": "docker",
         "description": "Docker CLI for container management"
       },
       "docker-compose": {
         "command": "docker-compose",
         "description": "Docker Compose for multi-container apps"
       },
       "web-search": {
         "command": "npx",
         "args": ["-y", "@modelcontextprotocol/server-brave-search"],
         "env": {
           "BRAVE_API_KEY": "${input:brave_api_key}"
         },
         "description": "Web search via Brave Search API"
       }
     },
     "inputs": [
       {
         "type": "promptString",
         "id": "brave_api_key",
         "description": "Brave Search API Key",
         "password": true
       }
     ]
   }
   ```

3. **Enable and Start MCP Servers**:
   - Open VS Code Command Palette (`Cmd/Ctrl + Shift + P`)
   - Run: `MCP: List Servers`
   - Start the servers you need
   - When prompted, enter your Brave Search API key

## Using Docker with .NET Aspire

.NET Aspire automatically manages Docker containers for development dependencies. When you run the AppHost:

```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

Aspire will automatically start:
- **Cosmos DB Emulator**: Local Azure Cosmos DB (port 8081)
- **Azurite**: Local Azure Storage emulator (ports 10000-10002)

You can monitor and manage these containers using:
```bash
# List running containers
docker ps

# View container logs
docker logs <container-id>

# Access the Aspire dashboard (usually https://localhost:17001)
# This provides a UI to monitor all services and containers
```

## How Copilot Uses These Tools

Once configured, GitHub Copilot coding agent can:

1. **Manage Docker Containers**:
   ```
   @copilot list all running docker containers
   @copilot show me the logs for the Cosmos DB emulator container
   ```

2. **Debug Container Issues**:
   ```
   @copilot inspect the network configuration of the aspire containers
   @copilot check if the Azurite storage emulator is healthy
   ```

3. **Search for Solutions**:
   ```
   @copilot search for how to configure Cosmos DB partition keys in .NET
   @copilot find examples of Azure OpenAI structured output in C#
   ```

4. **Build and Test**:
   ```
   @copilot build a docker image for the DocumentService
   @copilot create a Dockerfile for production deployment
   ```

## Troubleshooting

### Docker Commands Not Working
- Ensure Docker Desktop is running
- Verify Docker CLI is in your PATH: `docker --version`
- Check Copilot has proper permissions

### Web Search Not Working
- Verify Brave API key is set correctly
- Check Node.js is installed: `node --version`
- Try manually: `npx -y @modelcontextprotocol/server-brave-search`

### MCP Servers Not Listed
- Update VS Code to the latest version
- Update GitHub Copilot extension
- Check VS Code settings: `chat.agent.enabled` should be `true`

## Security Considerations

- **API Keys**: Never commit API keys to the repository
- **Docker Access**: MCP servers run with your local Docker permissions
- **Web Search**: Queries are sent to Brave Search API
- **Secrets**: Use GitHub secrets (prefixed with `COPILOT_MCP_`) for repository-level access

## Additional Resources

- [GitHub Docs: Extending Copilot with MCP](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent/extend-coding-agent-with-mcp)
- [MCP Specification](https://modelcontextprotocol.io/)
- [Brave Search API](https://brave.com/search/api/)
- [Docker Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)

## Notes

- This MCP configuration is designed for GitHub Copilot coding agent running in GitHub Codespaces or as part of GitHub's coding agent execution environment
- Local VS Code usage requires additional setup (see "For Developers" section)
- The web search MCP server requires a Brave Search API key (free tier available)
- Docker and Docker Compose must be available in the execution environment
