# GitHub Copilot Instructions for Can I Haz Houze

## Project Overview

Can I Haz Houze is a mortgage approval system built with **.NET 9.0** and **.NET Aspire 9.3.1**. It's a distributed microservices application that combines document management, ledger tracking, CRM functionality, and AI-powered mortgage approval decisions.

## MCP Server Configuration

This repository is configured with Model Context Protocol (MCP) servers that extend Copilot's capabilities:

- **Docker**: Run Docker commands for container management and debugging
- **Docker Compose**: Support for Docker Compose commands (when needed)
- **Web Search**: Search the web using Brave Search API for documentation, examples, and solutions

For detailed setup instructions, see [MCP_SETUP.md](MCP_SETUP.md).

### Quick Start with MCP Tools

```bash
# Aspire automatically starts containers when you run:
cd src
dotnet run --project CanIHazHouze.AppHost

# With MCP configured, you can ask Copilot to:
# - List running containers: "show me all docker containers"
# - Check logs: "what are the latest logs from the cosmos container"
# - Search docs: "search for .NET Aspire cosmos db configuration"
```

## Architecture

### Multi-Service Application
This is a .NET Aspire distributed application with the following services:

- **AppHost** (`CanIHazHouze.AppHost`): Orchestrator for all services
- **DocumentService** (`CanIHazHouze.DocumentService`): Handles document uploads and AI-powered analysis
- **LedgerService** (`CanIHazHouze.LedgerService`): Manages financial transactions
- **MortgageApprover** (`CanIHazHouze.MortgageApprover`): Automated mortgage approval workflow
- **CrmService** (`CanIHazHouze.CrmService`): Customer relationship management
- **Web** (`CanIHazHouze.Web`): Blazor-based web frontend
- **ServiceDefaults** (`CanIHazHouze.ServiceDefaults`): Shared configurations and defaults

### Technology Stack

- **Framework**: .NET 9.0
- **Orchestration**: .NET Aspire 9.3.1
- **Database**: Azure Cosmos DB (with local emulator for development)
- **Storage**: Azure Blob Storage
- **AI**: Azure OpenAI (GPT-4o-mini model)
- **Testing**: xUnit v3, Aspire.Hosting.Testing, Microsoft.AspNetCore.Mvc.Testing
- **Deployment**: Azure Developer CLI (azd) to Azure Container Apps

## Development Guidelines

### Project Structure
- All source code is in the `/src` directory
- Solution file: `src/CanIHazHouze.sln`
- Tests are located in `src/CanIHazHouze.Tests`
- Deployment scripts are in `/scripts`

### Building and Running

#### Local Development
```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

#### Running Tests
```bash
cd src
dotnet test
```

#### Building
```bash
cd src
dotnet build
```

**Note on Docker**: .NET Aspire automatically manages Docker containers for development dependencies (Cosmos DB emulator, Azurite storage emulator) when you run the AppHost. You don't need to manually start these services.

#### GitHub Codespaces
This repository includes a devcontainer configuration (`.devcontainer/devcontainer.json`) that provides:
- Docker-in-Docker support for Aspire orchestration
- .NET 9.0 SDK pre-installed
- Azure CLI and Azure Developer CLI (azd)
- All necessary VS Code extensions

When using GitHub Codespaces, Docker is automatically available and Aspire can orchestrate containers.

### Code Conventions

1. **Nullable Reference Types**: Enabled across all projects (`<Nullable>enable</Nullable>`)
2. **Implicit Usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
3. **Target Framework**: Always use `net9.0`
4. **Naming**: Use PascalCase for public members, camelCase for private fields
5. **Async/Await**: Use async patterns consistently for I/O operations

### Azure Configuration

#### Required Services
- **Azure OpenAI**: Required for document analysis and AI features
  - Model: `gpt-4o-mini` (version 2024-07-18)
  - Connection string format: `Endpoint=https://your-resource.openai.azure.com/`
  - Authentication: `DefaultAzureCredential` (no API key support)
  - Store in user secrets: `dotnet user-secrets set "ConnectionStrings:openai" "<connection-string>"`

- **Azure Cosmos DB**: Used for data persistence
  - Local development uses Cosmos DB Emulator (runs in Docker via Aspire)
  
- **Azure Blob Storage**: Used for document storage
  - Configured via Aspire in production

#### Deployment with azd
```bash
azd up  # First time or full deploy
azd deploy  # Deploy code changes only
azd down  # Clean up Azure resources
```

**Post-deployment**: The `scripts/postdeploy.sh` hook automatically configures local development after `azd up`:
- Enables public network access (development only)
- Retrieves Azure OpenAI endpoint and key
- Sets up local user secrets automatically

### Testing Strategy

- **Integration Tests**: Use `Aspire.Hosting.Testing` for testing distributed applications
- **Unit Tests**: Use xUnit v3
- **Test Projects**: Should reference both AppHost and individual service projects
- **In-Memory Testing**: Use `Microsoft.EntityFrameworkCore.InMemory` for database mocking when appropriate

### Documentation

- Start with `README.md` for setup and repository orientation
- Use `../docs/DOCUMENT_WORKFLOW.md` for end-to-end document lifecycle behavior
- Use service docs in `src/*/README.md` for service-specific behavior
- Use `../docs/PRODUCTION_DEPLOYMENT_GUIDE.md` for deployment and production behavior

## Best Practices for Issues

### Good Issues for Copilot
- Bug fixes in specific services
- Adding new API endpoints
- Refactoring small code segments
- Improving test coverage
- Updating documentation
- UI/UX improvements in the Blazor web frontend
- Adding new features to existing services

### Issues to Avoid
- Major architectural changes
- Cross-service refactoring
- Security-critical changes requiring domain expertise
- Complex business logic requiring deep mortgage industry knowledge
- Changes requiring detailed Azure infrastructure modifications

### Issue Requirements
When creating issues for Copilot, include:
1. **Clear description** of what needs to be done
2. **Acceptance criteria** (e.g., "must include unit tests", "must update API documentation")
3. **Affected files or services** (e.g., "in DocumentService", "in the Web frontend")
4. **Context**: Reference related code or documentation files

## Service-Specific Guidelines

### DocumentService
- Uses Azure OpenAI for document analysis
- Handles PDF uploads via Azure Blob Storage
- Provides mortgage document verification endpoints
- AI features: metadata extraction, tag suggestions, document classification

### LedgerService
- Manages financial transactions
- Provides mortgage calculation helpers
- Integrates with document verification results

### MortgageApprover
- Orchestrates the approval workflow
- Verifies document completeness
- Makes AI-assisted approval decisions

### CrmService
- Customer relationship management
- Tracks customer interactions
- Provides customer data to other services

### Web (Frontend)
- Built with Blazor
- Uses a fun, informal tone similar to the README:
  - Playful section headings (e.g., "The Boring Stuff", "The Fun Part!")
  - Emojis encouraged (🏠, 💰, 📄, 🤖)
  - Humorous comments and explanations
  - Example: "A mortgage approval app so smart, even your credit score gets jealous 🤖💳"
- Provides UI for all service interactions
- Includes API documentation page at `/api-docs`

## Common Tasks

### Adding a New Service
1. Create new project in `/src` using `Microsoft.NET.Sdk.Web`
2. Add reference to `ServiceDefaults` project
3. Register in `AppHost/Program.cs`
4. Add corresponding tests in `CanIHazHouze.Tests`
5. Update solution file

### Adding Azure OpenAI Features
1. Inject `IOpenAIClient` in your service
2. Use structured output patterns with `ChatCompletionOptions`
3. Handle errors gracefully (OpenAI calls can fail)
4. Add appropriate logging

### Modifying Data Models
1. Update model in appropriate service
2. Consider Cosmos DB partition key requirements
3. Update tests
4. Document breaking changes

### Updating Dependencies
1. Update all Aspire packages together (they must be same version)
2. Test locally before deploying
3. Check for breaking changes in release notes

## Debugging Tips

- Use the Aspire dashboard (usually at `https://localhost:17001`)
- Check service logs in the dashboard
- Verify secrets: `dotnet user-secrets list` in AppHost directory
- For Docker issues (Cosmos emulator), ensure Docker Desktop is running
- Check `.gitignore` before committing to avoid including build artifacts or secrets

## Security Considerations

- **Never commit secrets**: Use `dotnet user-secrets` for local development
- **Connection strings**: Always use secure connection strings in production
- **Azure OpenAI auth**: Use managed identities/RBAC (`DefaultAzureCredential`) instead of API keys
- **Public network access**: 
  - The `scripts/enable-public-access.sh` script enables public endpoints on Storage Account and Cosmos DB
  - **WARNING**: This is for DEVELOPMENT/TESTING ONLY and should NOT be used in production
  - Production deployments should use private endpoints and managed identities
  - The script is automatically run by the `postdeploy` hook to facilitate local development after Azure deployment
- **Authentication**: Add authentication/authorization when deploying to production

## Culture and Currency
The application is designed for US markets:
- Use `en-US` culture for currency formatting
- Display currency as USD ($)
- If you encounter "¤" symbols, set culture explicitly in `Program.cs`

## Additional Resources

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Azure Developer CLI Docs](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/)
- [Azure OpenAI Service](https://azure.microsoft.com/en-us/products/cognitive-services/openai-service)
- Project README.md for detailed setup instructions
- Video walkthrough: https://youtu.be/FjfPg8VdgfA
