# Dev Container Configuration

This directory contains the configuration for GitHub Codespaces and VS Code Dev Containers.

## What's Included

### Base Image
- **Microsoft .NET 9.0 Dev Container**: Includes .NET 9.0 SDK and development tools

### Features
- **Docker-in-Docker**: Enables running Docker commands and containers within Codespaces
  - Required for .NET Aspire to orchestrate Cosmos DB emulator and Azurite
  - Allows Copilot to use Docker MCP commands
- **Azure CLI**: For managing Azure resources
- **Azure Developer CLI (azd)**: For deploying with `azd up`

### VS Code Extensions
- **C# Dev Kit & C# Extension**: Full C# development support
- **Docker Extension**: Container management UI
- **Azure Developer Extension**: Integrated azd support
- **GitHub Copilot & Copilot Chat**: AI-powered coding assistance

### Port Forwarding
Automatically forwards these ports:
- `17001` - Aspire Dashboard (notifies on auto-forward)
- `8081` - Cosmos DB Emulator
- `10000-10002` - Azurite Storage Emulator (Blob, Queue, Table)

## Usage

### In GitHub Codespaces
1. Click "Code" → "Codespaces" → "Create codespace on [branch]"
2. Codespace will automatically build with Docker support
3. Run the AppHost: `cd src && dotnet run --project CanIHazHouze.AppHost`
4. Aspire will automatically start Docker containers for emulators

### For Copilot Coding Agent
**Note**: As of now, GitHub Copilot coding agent execution environment does not automatically use the devcontainer configuration. However:
- Docker IS available in the Copilot agent execution environment
- Copilot can use Docker commands via the MCP configuration in `.github/copilot-mcp.json`
- This devcontainer is primarily for developers using Codespaces or VS Code Dev Containers

GitHub is working on better integration between Copilot coding agent and devcontainer configurations.

### In VS Code Locally
1. Install "Dev Containers" extension
2. Open the repository
3. Command Palette → "Dev Containers: Reopen in Container"
4. Container will build with all features enabled

## How Docker Works with Aspire

When you run the AppHost project:
```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

.NET Aspire automatically:
1. Detects Docker is available
2. Starts Cosmos DB emulator container
3. Starts Azurite storage emulator container
4. Orchestrates networking between services
5. Provides the Aspire Dashboard at https://localhost:17001

You don't need to manually start containers - Aspire handles everything!

## Troubleshooting

### Docker not available
If Docker commands fail:
- In Codespaces: Rebuild the codespace (Codespace menu → Rebuild Container)
- In VS Code: Ensure Docker Desktop is running on your machine

### Aspire can't start containers
- Verify Docker is running: `docker ps`
- Check Docker daemon status: `docker info`
- Restart the dev container if needed

## Additional Resources
- [Dev Containers Documentation](https://containers.dev/)
- [GitHub Codespaces Docs](https://docs.github.com/en/codespaces)
- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
