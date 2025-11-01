# GitHub Actions Workflows

## Build and Test Workflow

The `build-and-test.yml` workflow runs on:
- Push to `main` or `develop` branches
- Pull requests targeting `main` or `develop` branches
- Manual trigger via workflow_dispatch

### What it does:

1. **Checkout code** - Gets the latest code from the repository
2. **Setup .NET 9.0** - Installs the required .NET SDK
3. **Restore dependencies** - Downloads all NuGet packages
4. **Build solution** - Compiles all projects in Release mode
5. **Run unit tests** - Executes MCP Server unit tests that don't require infrastructure

### Test Coverage:

✅ **Tests that run in CI:**
- MCP Server unit tests (tool registration, resource management, JSON schema generation)
- These tests validate the core MCP functionality without requiring external dependencies

❌ **Tests NOT run in CI:**
- Integration tests requiring Cosmos DB
- Integration tests requiring Azure Storage/Blob Storage
- Integration tests requiring Azure OpenAI
- End-to-end tests requiring full infrastructure

### Local Testing:

To run all tests locally with infrastructure:

```bash
# Start Aspire AppHost (includes Cosmos DB emulator, Azurite, etc.)
dotnet run --project src/CanIHazHouze.AppHost

# In another terminal, run all tests
cd src
dotnet test
```

To run only unit tests (no infrastructure needed):

```bash
cd src
dotnet test --filter "FullyQualifiedName~MCPServerTests"
```

### Notes:

- The workflow uses .NET 9.0 as specified in the project files
- Tests run in Release configuration for consistency with production builds
- Integration tests are intentionally excluded from CI as they require infrastructure setup
- The MCP smoke test validates all 4 services expose their tools correctly (24 total tools)
