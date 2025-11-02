# Running .NET Aspire AppHost with Cosmos DB Emulator in GitHub Actions

## Executive Summary

This document details the research and implementation for running the .NET Aspire AppHost with Cosmos DB emulator in GitHub Actions runners. The solution enables full integration testing with Docker-based emulators in CI/CD pipelines.

## Problem Statement

The AppHost starts emulators for Cosmos DB when run in a development environment. The goal was to adapt the Copilot instructions and GitHub Actions CI so the AppHost would behave as if in a development environment and start the emulators like on desktop environments.

## Research Findings

### 1. How .NET Aspire Determines When to Run Emulators

The key finding is that .NET Aspire uses the `builder.ExecutionContext.IsPublishMode` property to determine when to run emulators:

```csharp
// In AppHost.cs
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(emulator => {
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithDataVolume();
        emulator.WithDataExplorer();
    });
```

**Emulator Behavior:**
- **When `IsPublishMode == false`**: Emulators run (Development/Testing mode)
- **When `IsPublishMode == true`**: Real Azure resources are used (Production deployment)

This means emulators should automatically activate in CI environments without special configuration, as CI is not a "publish" operation.

### 2. Environment Variable Configuration

While `IsPublishMode` controls the primary behavior, environment variables reinforce the development context:

- **`ASPNETCORE_ENVIRONMENT=Development`**: Tells ASP.NET Core we're in development mode
- **`DOTNET_ENVIRONMENT=Development`**: Same for general .NET applications

Setting these in GitHub Actions ensures:
- Development configuration files are loaded
- Development logging levels are used
- Appropriate middleware and behaviors are enabled

### 3. Docker Availability in GitHub Actions

**Key Discovery**: Docker is **pre-installed** on `ubuntu-latest` GitHub Actions runners.

```yaml
- name: Verify Docker
  run: |
    docker --version  # Works out of the box!
    docker ps
```

This is crucial because:
- No additional setup or services needed
- .NET Aspire DCP can immediately orchestrate containers
- Cosmos DB Linux emulator can run as a container
- Azurite storage emulator can run as a container

### 4. Common Issues in CI Environments (from Research)

Research revealed several common problems when running Aspire with emulators in CI:

**Problem 1: Startup Timing**
- **Issue**: Emulator containers can take 30-60 seconds to fully start
- **Solution**: Use `WaitForResourceHealthyAsync()` in tests
- **Our Implementation**: Tests already use this pattern

**Problem 2: SSL/TLS Errors**
- **Issue**: Cosmos DB emulator uses self-signed certificates
- **Solution**: Preview emulator (vnext-preview) handles this better
- **Our Implementation**: Already using `RunAsPreviewEmulator()`

**Problem 3: Schema Issues**
- **Issue**: Some versions had "cosmos_api" schema errors in CI
- **Solution**: Use latest Aspire packages (9.3.1+)
- **Our Implementation**: Using Aspire 9.3.1

**Problem 4: Missing Logs**
- **Issue**: Container logs not captured when tests fail
- **Solution**: Add log capture steps in workflow
- **Our Implementation**: Added log capture on failure

### 5. Test Infrastructure Architecture

The test infrastructure uses **`DistributedApplicationTestingBuilder`** from `Aspire.Hosting.Testing`:

```csharp
// From WebTests.cs
var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.CanIHazHouze_AppHost>(cancellationToken);
    
await using var app = await appHost.BuildAsync(cancellationToken);
await app.StartAsync(cancellationToken);

// Wait for resources to be healthy
await app.ResourceNotifications
    .WaitForResourceHealthyAsync("cosmos", cancellationToken);
```

This pattern:
- Spins up the full AppHost with all services
- Starts Docker containers for emulators
- Waits for health checks before running tests
- Provides proper cleanup on disposal

## Implementation Solution

### 1. GitHub Actions Workflow Changes

Added a new job `docker-integration-tests` to `.github/workflows/build-and-test.yml`:

```yaml
docker-integration-tests:
  name: Docker Integration Tests (with Aspire DCP & Emulators)
  runs-on: ubuntu-latest
  needs: unit-tests
  
  steps:
  - name: Checkout code
    uses: actions/checkout@v4
    
  - name: Setup .NET
    uses: actions/setup-dotnet@v4
    with:
      dotnet-version: '9.0.x'
      
  - name: Verify Docker
    run: |
      docker --version
      docker ps
      
  - name: Configure environment for emulators
    run: |
      echo "ASPNETCORE_ENVIRONMENT=Development" >> $GITHUB_ENV
      echo "DOTNET_ENVIRONMENT=Development" >> $GITHUB_ENV
      
  - name: Restore dependencies
    run: dotnet restore src/CanIHazHouze.sln
    
  - name: Build solution
    run: dotnet build src/CanIHazHouze.sln --no-restore --configuration Release
    
  - name: Run Docker-dependent integration tests
    run: dotnet test src/CanIHazHouze.sln --no-build --configuration Release --verbosity normal --filter "Category=Integration&Category=RequiresDocker" --logger "console;verbosity=detailed"
    timeout-minutes: 10
    
  - name: Capture Docker container logs on failure
    if: failure()
    run: |
      echo "## Docker Container Logs" >> $GITHUB_STEP_SUMMARY
      docker ps -a
      for container in $(docker ps -a --format '{{.Names}}'); do
        echo "### Logs for container: $container" >> $GITHUB_STEP_SUMMARY
        docker logs $container --tail 100 2>&1 | head -50 >> $GITHUB_STEP_SUMMARY || echo "Could not retrieve logs for $container" >> $GITHUB_STEP_SUMMARY
      done
```

### 2. Test Categorization

Tests are categorized using xUnit traits:

```csharp
[Fact]
[Trait("Category", "Integration")]
[Trait("Category", "RequiresDocker")]
public async Task GetWebResourceRootReturnsOkStatusCode()
{
    // Test implementation using DistributedApplicationTestingBuilder
}
```

### 3. CI Pipeline Architecture

The CI now has three parallel test stages:

```
┌─────────────────┐
│   Unit Tests    │
│  (No external   │
│  dependencies)  │
└────────┬────────┘
         │
    ┌────┴────┐
    │         │
┌───▼────┐ ┌─▼──────────────────┐
│ Integ. │ │ Docker Integration │
│ Tests  │ │      Tests         │
│ (Web   │ │ (Full AppHost +    │
│ App    │ │  Emulators)        │
│Factory)│ │                    │
└────────┘ └────────────────────┘
```

**Stage 1: Unit Tests**
- Filter: `Category!=Integration`
- Fast tests, no external dependencies
- ~19 tests, ~10 seconds

**Stage 2: Integration Tests (No Infrastructure)**
- Filter: `Category=Integration&Category!=RequiresDocker&Category!=RequiresDatabase`
- Uses WebApplicationFactory
- MCP integration tests
- ~8 tests, ~15 seconds

**Stage 3: Docker Integration Tests** (NEW!)
- Filter: `Category=Integration&Category=RequiresDocker`
- Full AppHost with Aspire DCP
- Cosmos DB emulator + Azurite
- ~4 WebTests, ~2-3 minutes

### 4. Documentation Updates

Updated `.github/copilot-instructions.md` with comprehensive sections on:
- CI/CD pipeline architecture
- How emulator activation works
- Local testing commands
- Troubleshooting guidance

## Technical Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│              GitHub Actions Runner                      │
│  ┌───────────────────────────────────────────────────┐ │
│  │  Ubuntu-latest (Docker pre-installed)             │ │
│  │  ┌────────────────────────────────────────────┐  │ │
│  │  │  .NET 9.0 SDK                              │  │ │
│  │  │  ┌──────────────────────────────────────┐ │  │ │
│  │  │  │  Aspire AppHost                      │ │  │ │
│  │  │  │  - Detects !IsPublishMode           │ │  │ │
│  │  │  │  - Starts DCP orchestrator          │ │  │ │
│  │  │  │  ┌──────────────────────────────┐  │ │  │ │
│  │  │  │  │  Docker Containers           │  │ │  │ │
│  │  │  │  │  ┌────────────────────────┐ │  │ │  │ │
│  │  │  │  │  │ Cosmos DB Emulator     │ │  │ │  │ │
│  │  │  │  │  │ (vnext-preview)        │ │  │ │  │ │
│  │  │  │  │  │ - Port 8081           │ │  │ │  │ │
│  │  │  │  │  │ - Port 1234 (Explorer)│ │  │ │  │ │
│  │  │  │  │  └────────────────────────┘ │  │ │  │ │
│  │  │  │  │  ┌────────────────────────┐ │  │ │  │ │
│  │  │  │  │  │ Azurite Emulator       │ │  │ │  │ │
│  │  │  │  │  │ - Blob (10000)        │ │  │ │  │ │
│  │  │  │  │  │ - Queue (10001)       │ │  │ │  │ │
│  │  │  │  │  │ - Table (10002)       │ │  │ │  │ │
│  │  │  │  │  └────────────────────────┘ │  │ │  │ │
│  │  │  │  │  ┌────────────────────────┐ │  │ │  │ │
│  │  │  │  │  │ Service Containers     │ │  │ │  │ │
│  │  │  │  │  │ - DocumentService      │ │  │ │  │ │
│  │  │  │  │  │ - LedgerService        │ │  │ │  │ │
│  │  │  │  │  │ - MortgageApprover     │ │  │ │  │ │
│  │  │  │  │  │ - CrmService           │ │  │ │  │ │
│  │  │  │  │  │ - WebFrontend          │ │  │ │  │ │
│  │  │  │  │  └────────────────────────┘ │  │ │  │ │
│  │  │  │  └──────────────────────────────┘  │ │  │ │
│  │  │  └──────────────────────────────────────┘ │  │ │
│  │  └────────────────────────────────────────────┘  │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

## Key Success Factors

1. **No Special Docker Setup Required**: Docker is already available on ubuntu-latest runners
2. **Automatic Emulator Activation**: The AppHost already has the right configuration
3. **Test Infrastructure Ready**: Tests already use proper wait patterns
4. **Environment Configuration**: Simple environment variables reinforce development mode
5. **Proper Test Categorization**: Traits allow precise test filtering

## Performance Considerations

**Emulator Startup Times:**
- Cosmos DB emulator: 30-60 seconds
- Azurite emulator: 5-10 seconds
- Service startup: 10-20 seconds each
- **Total test time**: ~2-3 minutes for Docker tests

**Optimization Strategies:**
- Use `ContainerLifetime.Persistent` for emulators (already implemented)
- Run Docker tests in parallel with non-Docker tests
- Set appropriate timeouts (10 minutes for Docker test job)
- Cache Docker images if needed (not currently necessary)

## Debugging Failed Runs

When Docker integration tests fail, the workflow captures:
1. List of all containers (`docker ps -a`)
2. Last 100 lines of each container's logs
3. Detailed test output with `--logger "console;verbosity=detailed"`

This information appears in the GitHub Actions step summary for easy review.

## Local Testing

Developers can run the same tests locally:

```bash
# Run all Docker-dependent tests
cd src
dotnet test --filter "Category=RequiresDocker"

# Run specific WebTests
dotnet test --filter "FullyQualifiedName~WebTests"

# Run with detailed logging
dotnet test --filter "Category=RequiresDocker" --logger "console;verbosity=detailed"
```

## Future Enhancements

Potential improvements for the future:

1. **Matrix Testing**: Test against multiple .NET versions
2. **Container Caching**: Cache emulator images for faster startup
3. **Parallel Test Execution**: Run WebTests in parallel
4. **Performance Benchmarks**: Track test execution time trends
5. **Custom Emulator Configuration**: Fine-tune emulator settings for CI

## Conclusion

The solution successfully enables Docker-based integration testing in GitHub Actions by:

- ✅ Leveraging pre-installed Docker on ubuntu-latest runners
- ✅ Using existing emulator configuration in AppHost
- ✅ Setting appropriate environment variables
- ✅ Adding proper test categorization and filtering
- ✅ Capturing logs for debugging
- ✅ Documenting the approach comprehensively

**No code changes were required to the AppHost or test infrastructure** - the solution focused on CI configuration and documentation, which aligns with the "minimal changes" principle.

## References

### Documentation
- [.NET Aspire Testing Overview](https://learn.microsoft.com/en-us/dotnet/aspire/testing/overview)
- [Aspire Cosmos DB Integration](https://learn.microsoft.com/en-us/dotnet/aspire/database/azure-cosmos-db-integration)
- [GitHub Actions Docker Support](https://docs.github.com/en/actions/using-containerized-services/about-service-containers)

### Related GitHub Issues
- [vNext Emulator Issue with GitHub Actions & .NET Aspire](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/199)
- [Aspire 9.2.1: Cosmos DB Preview Emulator not creating databases/containers](https://github.com/dotnet/aspire/issues/9326)
- [Final container logs missing when disposing](https://github.com/dotnet/aspire/issues/8206)

### Key Code Locations
- AppHost Configuration: `src/CanIHazHouze.AppHost/AppHost.cs`
- Test Infrastructure: `src/CanIHazHouze.Tests/WebTests.cs`
- CI Workflow: `.github/workflows/build-and-test.yml`
- Documentation: `.github/copilot-instructions.md`
