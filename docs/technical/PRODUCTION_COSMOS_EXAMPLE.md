# Production Configuration Example for Your Mortgage App

Here's how you can modify your `AppHost.cs` to support both development and production configurations:

## Enhanced AppHost.cs with Production Configuration

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Environment-specific configuration
var isProduction = builder.Environment.IsProduction();
var isDevelopment = builder.Environment.IsDevelopment();

// Configuration parameters for production
var cosmosRegion = builder.AddParameter("cosmos-region", "East US");
var cosmosFreeTier = builder.AddParameter("cosmos-free-tier", "true");
var cosmosConsistency = builder.AddParameter("cosmos-consistency", "Session");

// Add Azure Cosmos DB with different configurations for dev vs prod
var cosmos = builder.AddAzureCosmosDB("cosmos");

// Configure for local development
if (isDevelopment)
{
    // Use the new Linux-based emulator that supports Apple Silicon
    #pragma warning disable ASPIRECOSMOSDB001
    cosmos.RunAsPreviewEmulator(emulator =>
    {
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithDataVolume();
        emulator.WithDataExplorer();
    });
    #pragma warning restore ASPIRECOSMOSDB001
}

// Configure for production deployment 
cosmos.ConfigureInfrastructure(infra =>
{
    var cosmosDbAccount = infra.GetProvisionableResources()
                               .OfType<CosmosDBAccount>()
                               .Single();

    // Production-specific configuration
    if (isProduction)
    {
        // Multi-region setup for production
        cosmosDbAccount.Locations = new[]
        {
            new Location
            {
                LocationName = cosmosRegion.AsProvisioningParameter(infra),
                FailoverPriority = 0,
                IsZoneRedundant = false
            }
        };

        // Production consistency and reliability
        cosmosDbAccount.ConsistencyPolicy = new ConsistencyPolicy
        {
            DefaultConsistencyLevel = 
                cosmosConsistency.AsProvisioningParameter(infra) == "Strong" 
                    ? DefaultConsistencyLevel.Strong 
                    : DefaultConsistencyLevel.Session
        };

        // Production backup policy
        cosmosDbAccount.BackupPolicy = new PeriodicModeBackupPolicy
        {
            PeriodicModeProperties = new PeriodicModeProperties
            {
                BackupIntervalInMinutes = 240, // 4 hours
                BackupRetentionIntervalInHours = 720, // 30 days
                BackupStorageRedundancy = BackupStorageRedundancy.Geo
            }
        };

        // Production tags
        cosmosDbAccount.Tags.Add("Environment", "Production");
        cosmosDbAccount.Tags.Add("Application", "MortgageApp");
        cosmosDbAccount.Tags.Add("Owner", "DevTeam");
    }
    else
    {
        // Development/staging configuration
        cosmosDbAccount.EnableFreeTier = cosmosFreeTier.AsProvisioningParameter(infra);
        
        // Development tags
        cosmosDbAccount.Tags.Add("Environment", "Development");
        cosmosDbAccount.Tags.Add("Application", "MortgageApp");
        cosmosDbAccount.Tags.Add("CostCenter", "Development");
    }
});

// Create shared database with separate containers for each service
var houzeDatabase = cosmos.AddCosmosDatabase("houze");

// Add containers with username as partition key for optimal RU sharing
var documentsContainer = houzeDatabase.AddContainer("documents", "/owner");
var ledgersContainer = houzeDatabase.AddContainer("ledgers", "/owner"); 
var mortgagesContainer = houzeDatabase.AddContainer("mortgages", "/owner");

// Rest of your service configuration remains the same...
var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithReference(cosmos)
    .WithHttpHealthCheck("/health");

var ledgerService = builder.AddProject<Projects.CanIHazHouze_LedgerService>("ledgerservice")
    .WithReference(cosmos)
    .WithHttpHealthCheck("/health");

var mortgageService = builder.AddProject<Projects.CanIHazHouze_MortgageApprover>("mortgageapprover")
    .WithReference(cosmos)
    .WithReference(documentService)
    .WithReference(ledgerService)
    .WithHttpHealthCheck("/health")
    .WaitFor(documentService)
    .WaitFor(ledgerService);

builder.AddProject<Projects.CanIHazHouze_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(documentService)
    .WithReference(ledgerService)
    .WithReference(mortgageService)
    .WaitFor(documentService)
    .WaitFor(ledgerService)
    .WaitFor(mortgageService);

builder.Build().Run();
```

## Alternative: Serverless Configuration

If you prefer serverless mode for cost optimization:

```csharp
// Serverless configuration - good for unpredictable workloads
cosmos.ConfigureInfrastructure(infra =>
{
    var cosmosDbAccount = infra.GetProvisionableResources()
                               .OfType<CosmosDBAccount>()
                               .Single();

    // Enable serverless mode
    cosmosDbAccount.Capabilities = new[]
    {
        new CosmosDBCapability { Name = "EnableServerless" }
    };

    // Session consistency (good for web apps)
    cosmosDbAccount.ConsistencyPolicy = new ConsistencyPolicy
    {
        DefaultConsistencyLevel = DefaultConsistencyLevel.Session
    };

    // Tags for cost tracking
    cosmosDbAccount.Tags.Add("Environment", "Production");
    cosmosDbAccount.Tags.Add("CapacityMode", "Serverless");
    cosmosDbAccount.Tags.Add("Application", "MortgageApp");
});
```

## Deployment Commands

For development:
```bash
# Run locally with emulator
dotnet run --project CanIHazHouze.AppHost

# Or with Visual Studio
# Press F5
```

For production deployment:
```bash
# Initialize Azure deployment (first time only)
azd init

# Set parameters
azd env set COSMOS_REGION "East US"
azd env set COSMOS_FREE_TIER "false"
azd env set COSMOS_CONSISTENCY "Session"

# Deploy to Azure
azd up
```

## Cost Optimization Tips

1. **Development**: Use free tier + serverless
2. **Staging**: Use serverless mode
3. **Production**: Use provisioned throughput with autoscale
4. **Monitor**: Use Azure Cost Management to track spending

## Environment Variables

Add these to your `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "cosmos": "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
  }
}
```

For production, this will be automatically configured by Azure with the real connection string.
