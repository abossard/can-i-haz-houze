# .NET Aspire 9 to 13 Upgrade Guide

## Overview

This document outlines the breaking changes, new features, and upgrade path from **Aspire 9.3.1** (currently in use) to **Aspire 13.0** (latest version released November 11, 2025).

**Key Change**: Aspire is now simply "Aspire" (not ".NET Aspire") - a full polyglot application platform supporting .NET, Python, and JavaScript as first-class citizens.

---

## 🆕 Major New Features

### Polyglot Platform Support
- **Python as first-class citizen**
  - `AddPythonApp`, `AddPythonModule`, `AddPythonExecutable` methods
  - Uvicorn integration for ASGI apps (FastAPI, Starlette, Quart)
  - Package managers: uv, pip, venv with auto-detection
  - VS Code debugging support
  - Automatic Dockerfile generation
  - Python version detection from `.python-version`, `pyproject.toml`, or venv

- **JavaScript as first-class citizen**
  - New unified `AddJavaScriptApp` replaces `AddNpmApp`
  - Package manager flexibility: npm, yarn, pnpm with auto-detection
  - Vite support via `AddViteApp`
  - Node.js refactored with `AddNodeApp`
  - Automatic Dockerfile generation
  - VS Code debugging support

- **Polyglot infrastructure**
  - Multiple connection string formats (URI, JDBC, individual properties)
  - Certificate trust across languages and containers
  - Simplified service URL environment variables

### CLI and Tooling
- **`aspire init`** - Interactive solution initialization
- **`aspire do`** - New pipeline system for build/publish/deploy workflows
  - Parallel execution
  - Dependency tracking
  - Extensible workflows
- **`aspire update --self`** - Update CLI itself
- **VS Code extension** - Full Aspire integration in VS Code
  - Multi-language debugging (C#, Python)
  - Project creation and integration management
  - Launch configuration
  - Deployment commands
- **Single-file AppHost support** - No project file needed
- **Automatic .NET SDK installation** (Preview)
- **Non-interactive mode** for CI/CD

### Container & Build Features
- **Container files as build artifacts** - Extract files from one container and copy to another
  - Example: Build frontend in one container, serve from backend
- **Dockerfile builder API** (experimental) - Programmatic Dockerfile generation
- **Certificate management** - Custom CA and dev certificate trust

### Dashboard Enhancements
- **Aspire MCP server** - AI assistants can query resources, telemetry, execute commands
- **Dynamic inputs and comboboxes** - Cascading dropdowns in interaction service
- **Polyglot language icons** - Visual indicators for C#, F#, Python, JavaScript
- **Improved accent colors** - Better visibility in dark/light themes
- **Health checks last run time** - Timestamp display

### App Model Improvements
- **C# file-based app support** - Run C# files without full projects
- **Network identifiers** - Context-aware endpoint resolution
- **Named references** - Control environment variable prefixes
- **Connection properties** - Access individual connection string components
- **Endpoint reference enhancements** - Network-aware URL resolution

### Deployment Improvements
- **Pipeline-based deployment** - Built on `aspire do`
  - Parallelization of independent operations
  - Granular step control
  - Pipeline diagnostics
- **Deployment state management** - Remembers Azure config between deployments
  - Subscription, resource group, location, tenant
  - Parameter values persist locally
  - Per-environment state tracking
- **Azure tenant selection** - Interactive multi-tenant support
- **Azure App Service enhancements**
  - Dashboard included by default
  - Application Insights integration

### New Integrations
- **.NET MAUI integration** - Orchestrate mobile apps alongside cloud services
  - Windows, Mac Catalyst, Android, iOS support
  - Device registration and platform validation

---

## ⚠️ Breaking Changes

### Package Renames
- `Aspire.Hosting.NodeJs` → `Aspire.Hosting.JavaScript`
  ```xml
  <!-- Before -->
  <PackageReference Include="Aspire.Hosting.NodeJs" Version="9.x.x" />
  
  <!-- After -->
  <PackageReference Include="Aspire.Hosting.JavaScript" Version="13.0.0" />
  ```

### Removed APIs
- **Publishing infrastructure** (replaced by `aspire do`)
  - `PublishingContext`, `PublishingCallbackAnnotation`
  - `DeployingContext`, `DeployingCallbackAnnotation`
  - `WithPublishingCallback`
  - `IDistributedApplicationPublisher`
  - All `PublishingExtensions` methods

- **Debugging APIs**
  - Old `WithDebugSupport` overload with `debugAdapterId`
  - `SupportsDebuggingAnnotation`

- **CLI flags**
  - `--watch` flag removed from `aspire run` (use `features.defaultWatchEnabled` config)

### Obsolete APIs (will be removed in future)
- **Lifecycle hooks** → Use `IDistributedApplicationEventingSubscriber`
  - `IDistributedApplicationLifecycleHook`
  - `AddLifecycleHook<T>()` → Use `AddEventingSubscriber<T>()`

- **Node.js APIs**
  - `AddNpmApp()` → Use `AddJavaScriptApp()` or `AddViteApp()`

### Changed Signatures
- **AllocatedEndpoint constructor**
  ```csharp
  // Before
  var endpoint = new AllocatedEndpoint("http", 8080, containerHostAddress: "localhost");
  
  // After
  var endpoint = new AllocatedEndpoint("http", 8080, networkIdentifier: NetworkIdentifier.Host);
  ```

- **ProcessArgumentValuesAsync** - `containerHostName` parameter removed

- **EndpointReference.GetValueAsync** - Now waits for allocation instead of throwing immediately

- **InteractionInput properties**
  - `MaxLength`: settable → init-only
  - `Options`: init-only → settable
  - `Placeholder`: settable → init-only

### Major Architectural Changes

#### Universal Container-to-Host Communication
- Leverages DCP's container tunnel capability
- `EndpointReference` resolution is context-aware
- Requires `ASPIRE_ENABLE_CONTAINER_TUNNEL=true` (experimental)

#### Refactored AddNodeApp API
```csharp
// Before (9.x)
builder.AddNodeApp(
    name: "frontend",
    scriptPath: "/absolute/path/to/app.js",
    workingDirectory: "/absolute/path/to");

// After (13.0)
builder.AddNodeApp(
    name: "frontend",
    appDirectory: "../frontend",
    scriptPath: "app.js");
```

### AppHost Template Changes
- **Simplified SDK declaration**
  ```xml
  <!-- Before (9.x) -->
  <Project Sdk="Microsoft.NET.Sdk">
    <Sdk Name="Aspire.AppHost.Sdk" Version="9.3.1" />
    <PackageReference Include="Aspire.Hosting.AppHost" Version="9.3.1" />
  </Project>
  
  <!-- After (13.0) -->
  <Project Sdk="Aspire.AppHost.Sdk/13.0.0">
    <!-- Aspire.Hosting.AppHost is now implicit -->
  </Project>
  ```

- **Target framework**: `net9.0` → `net10.0` (requires .NET 10 SDK)

---

## 📋 Upgrade Plan

### Prerequisites
1. **Install .NET 10 SDK**
   - Download from: https://dotnet.microsoft.com/download/dotnet/10.0
   - Required for Aspire 13.0

2. **Backup current state**
   ```bash
   git commit -am "Pre-Aspire 13 upgrade checkpoint"
   git tag aspire-9.3.1-baseline
   ```

### Step 1: Update Aspire CLI
```bash
# macOS/Linux
curl -sSL https://aspire.dev/install.sh | bash

# Windows (PowerShell)
irm https://aspire.dev/install.ps1 | iex

# Verify
aspire --version  # Should show 13.0.0
```

### Step 2: Update Project Packages
```bash
cd src/CanIHazHouze.AppHost
aspire update
```

This command will:
- Update `Aspire.AppHost.Sdk` version
- Update all Aspire NuGet packages to 13.0.0
- Handle dependency resolution
- Support Central Package Management (CPM)

### Step 3: Update AppHost Project File
The `aspire update` command should handle this automatically, but verify:

**Before** (`CanIHazHouze.AppHost.csproj`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Aspire.AppHost.Sdk" Version="9.3.1" />
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="9.3.1" />
    <PackageReference Include="Aspire.Hosting.Azure.CosmosDB" Version="9.3.1" />
    <!-- other packages -->
  </ItemGroup>
</Project>
```

**After**:
```xml
<Project Sdk="Aspire.AppHost.Sdk/13.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <!-- Aspire.Hosting.AppHost is now implicit -->
    <PackageReference Include="Aspire.Hosting.Azure.CosmosDB" Version="13.0.0" />
    <!-- other packages -->
  </ItemGroup>
</Project>
```

### Step 4: Update ServiceDefaults
```bash
cd src/CanIHazHouze.ServiceDefaults
dotnet add package Aspire.ServiceDefaults --version 13.0.0
```

### Step 5: Update All Service Projects
For each service (AgentService, DocumentService, LedgerService, MortgageApprover, CrmService, Web):
```bash
cd src/CanIHazHouze.{ServiceName}
dotnet add package Aspire.Hosting.* --version 13.0.0  # Update integration packages
```

### Step 6: Code Changes

#### Update Lifecycle Hooks (if any)
```csharp
// Before
public class MyLifecycleHook : IDistributedApplicationLifecycleHook
{
    public async Task BeforeStartAsync(
        DistributedApplicationModel model,
        CancellationToken cancellationToken)
    {
        // Logic
    }
}
builder.Services.TryAddLifecycleHook<MyLifecycleHook>();

// After
public class MyEventSubscriber : IDistributedApplicationEventingSubscriber
{
    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        eventing.Subscribe<BeforeStartEvent>((@event, ct) =>
        {
            var model = @event.Model;
            // Logic
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
builder.Services.TryAddEventingSubscriber<MyEventSubscriber>();
```

#### Update Publishing Callbacks (if any)
```csharp
// Before
var api = builder.AddProject<Projects.Api>("api")
    .WithPublishingCallback(async (context, cancellationToken) =>
    {
        await CustomDeployAsync(context, cancellationToken);
    });

// After
var api = builder.AddProject<Projects.Api>("api")
    .WithPipelineStepFactory(context =>
    {
        return new PipelineStep()
        {
            Name = "CustomDeployStep",
            Action = CustomDeployAsync,
            RequiredBySteps = [WellKnownPipelineSteps.Publish]
        };
    });
```

### Step 7: Update Target Framework
Update all `.csproj` files:
```xml
<TargetFramework>net9.0</TargetFramework>
→
<TargetFramework>net10.0</TargetFramework>
```

Or use global find/replace:
```bash
find src -name "*.csproj" -exec sed -i '' 's/net9.0/net10.0/g' {} \;
```

### Step 8: Build and Test
```bash
cd src
dotnet clean
dotnet build
dotnet test
```

### Step 9: Test Local Run
```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

Verify:
- All services start successfully
- Dashboard loads at https://localhost:17001
- Cosmos DB emulator connects
- Azure OpenAI integration works

### Step 10: Test Deployment
```bash
cd src
aspire deploy --clear-cache  # Clear old deployment state
```

Follow interactive prompts for Azure configuration.

### Step 11: Update CI/CD (if applicable)
Update GitHub Actions / Azure DevOps pipelines:
- Update .NET SDK version to 10.0
- Update Aspire CLI installation
- Consider using `aspire do` commands for granular control

---

## 🔧 Optional: Leverage New Features

### 1. Enable Python Support (if needed)
```csharp
// In AppHost.cs
var pythonWorker = builder.AddPythonApp("worker", "./worker", "main.py")
    .WithReference(cosmos);
```

### 2. Add VS Code Debugging Support
```bash
aspire config set vscode.enabled true
```

### 3. Enable Container Tunnel (Experimental)
```bash
export ASPIRE_ENABLE_CONTAINER_TUNNEL=true
```

### 4. Use Deployment State Management
Deployment configuration now persists automatically at:
- macOS/Linux: `$HOME/.aspire/deployments/<project-hash>/<environment>.json`
- Windows: `%USERPROFILE%\.aspire\deployments\<project-hash>\<environment>.json`

To reset:
```bash
aspire deploy --clear-cache
```

---

## 📚 Reference URLs

### Official Documentation
- **What's New in Aspire 13**: https://aspire.dev/whats-new/aspire-13/
- **Aspire Official Docs**: https://aspire.dev/
- **Microsoft Learn - Aspire**: https://learn.microsoft.com/dotnet/aspire/

### Release Information
- **GitHub Release**: https://github.com/dotnet/aspire/releases/tag/v13.0.0
- **Full Changelog**: https://github.com/dotnet/aspire/compare/v9.5.0...v13.0.0
- **Aspire Support Policy**: https://dotnet.microsoft.com/platform/support/policy/aspire

### Migration Guides
- **Breaking Changes**: https://learn.microsoft.com/dotnet/aspire/compatibility/breaking-changes
- **API Removal**: https://learn.microsoft.com/dotnet/aspire/compatibility/api-removal
- **Upgrade from 8.x to 9.x**: https://learn.microsoft.com/dotnet/aspire/get-started/upgrade-to-aspire-9

### CLI Documentation
- **aspire init**: https://aspire.dev/reference/cli/commands/aspire-init/
- **aspire update**: https://aspire.dev/reference/cli/commands/aspire-update/
- **aspire do**: https://aspire.dev/reference/cli/commands/aspire-do/

### Community
- **GitHub Discussions**: https://github.com/dotnet/aspire/discussions
- **Discord**: https://aka.ms/aspire-discord
- **Aspire Roadmap (2025-26)**: https://github.com/dotnet/aspire/discussions/10644
- **YouTube**: https://www.youtube.com/@aspiredotdev

### Version History
- **Aspire 9.5 Release**: https://www.infoq.com/news/2025/09/aspire-95-release/
- **Aspire 9.4 Release**: https://www.infoq.com/news/2025/08/dotnet-aspire-9-4-release/
- **Aspire 9.3 Docs**: https://learn.microsoft.com/dotnet/aspire/whats-new/dotnet-aspire-9.3

---

## ✅ Post-Upgrade Checklist

- [ ] .NET 10 SDK installed
- [ ] Aspire CLI updated to 13.0.0
- [ ] All NuGet packages updated to 13.0.0
- [ ] AppHost project file simplified
- [ ] Target framework updated to `net10.0`
- [ ] Lifecycle hooks migrated to event subscribers (if any)
- [ ] Publishing callbacks migrated to pipeline steps (if any)
- [ ] Local development works (`dotnet run`)
- [ ] All tests pass (`dotnet test`)
- [ ] Azure deployment works (`aspire deploy`)
- [ ] CI/CD pipeline updated (if applicable)
- [ ] Documentation updated
- [ ] Team notified of changes

---

## 🐛 Troubleshooting

### Build Errors After Upgrade

**Issue**: `error NU1102: Unable to find package Aspire.Hosting.AppHost`
**Solution**: Remove explicit `Aspire.Hosting.AppHost` reference - it's now implicit in SDK

**Issue**: `The target framework 'net10.0' is not supported`
**Solution**: Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0

### Runtime Errors

**Issue**: `AllocatedEndpoint constructor not found`
**Solution**: Update to new signature with `networkIdentifier` parameter

**Issue**: `WithPublishingCallback does not exist`
**Solution**: Migrate to `WithPipelineStepFactory` and `aspire do`

### Deployment Issues

**Issue**: Azure deployment prompts for subscription every time
**Solution**: Deployment state is now automatic - run `aspire deploy` once to save config

---

## 📊 Version Progression

- **Aspire 8.0** (May 21, 2024) - Initial GA
- **Aspire 8.1** (July 23, 2024)
- **Aspire 8.2** (August 29, 2024)
- **Aspire 9.0** (November 12, 2024) - Major version
- **Aspire 9.1** (February 25, 2025)
- **Aspire 9.2** (April 10, 2025)
- **Aspire 9.3** (May 19, 2025) ← **Current Version in Use**
- **Aspire 9.4** (July 29, 2025)
- **Aspire 9.5** (September 25, 2025)
- **Aspire 13.0** (November 11, 2025) ← **Target Version**

**Note**: Microsoft only supports the latest release. Once Aspire 13.0 is released, 9.5 support ends.

---

## 🎯 Recommended Timeline

1. **Week 1**: Development environment upgrade
   - Install .NET 10 SDK
   - Update Aspire CLI
   - Test local development

2. **Week 2**: Code migration and testing
   - Update packages
   - Migrate breaking changes
   - Run full test suite

3. **Week 3**: Staging deployment
   - Deploy to test environment
   - Validate all services
   - Performance testing

4. **Week 4**: Production upgrade
   - Production deployment
   - Monitor telemetry
   - Team training on new features

---

**Last Updated**: Based on information as of November 12, 2025
**Document Version**: 1.0
