# .NET Aspire Azure Cosmos DB Configuration Guide

## Overview

This document provides comprehensive information about configuring Azure Cosmos DB parameters in .NET Aspire applications when deploying to Azure. .NET Aspire uses the Azure.Provisioning libraries to generate Bicep templates that define your Azure resources.

## Key Concepts

### Local Development vs Production

- **Local Development**: Uses the Cosmos DB emulator when `.RunAsEmulator()` is called
- **Azure Production**: Uses real Azure Cosmos DB service, emulator setting is ignored
- **Deployment**: Azure resources are automatically provisioned by `azd up` command

### Configuration Methods

1. **Default Configuration**: Uses sensible defaults (Basic/Standard SKUs)
2. **Infrastructure Customization**: Use `.ConfigureInfrastructure()` to customize Azure resources
3. **Connection String**: Connect to existing Cosmos DB accounts

## Available Configuration Parameters

### Core Cosmos DB Account Properties

When using `.ConfigureInfrastructure()`, you can configure the following properties on the `CosmosDBAccount` object:

#### 1. Capacity Mode
```csharp
// Serverless mode (pay-per-request)
cosmosDbAccount.Capabilities = new[]
{
    new CosmosDBCapability { Name = "EnableServerless" }
};

// Provisioned throughput mode (default)
// No special configuration needed
```

#### 2. Consistency Policy
```csharp
cosmosDbAccount.ConsistencyPolicy = new ConsistencyPolicy
{
    DefaultConsistencyLevel = DefaultConsistencyLevel.Session, // Default
    // Options: Strong, BoundedStaleness, Session, ConsistentPrefix, Eventual
    MaxIntervalInSeconds = 300,
    MaxStalenessPrefix = 100000
};
```

#### 3. Geographic Distribution
```csharp
cosmosDbAccount.Locations = new[]
{
    new Location
    {
        LocationName = "East US",
        FailoverPriority = 0,
        IsZoneRedundant = false
    },
    new Location
    {
        LocationName = "West US 2",
        FailoverPriority = 1,
        IsZoneRedundant = false
    }
};

// Multi-region writes
cosmosDbAccount.EnableMultipleWriteLocations = true;
```

#### 4. API Kind
```csharp
cosmosDbAccount.Kind = CosmosDBAccountKind.GlobalDocumentDB; // NoSQL (default)
// Other options:
// CosmosDBAccountKind.MongoDB
// CosmosDBAccountKind.Parse
```

#### 5. Free Tier
```csharp
cosmosDbAccount.EnableFreeTier = true; // One per subscription
```

#### 6. Backup Policy
```csharp
cosmosDbAccount.BackupPolicy = new PeriodicModeBackupPolicy
{
    PeriodicModeProperties = new PeriodicModeProperties
    {
        BackupIntervalInMinutes = 240,
        BackupRetentionIntervalInHours = 8,
        BackupStorageRedundancy = BackupStorageRedundancy.Geo
    }
};
```

#### 7. Network Access
```csharp
cosmosDbAccount.IsVirtualNetworkFilterEnabled = true;
cosmosDbAccount.EnableAutomaticFailover = true;
cosmosDbAccount.DisableKeyBasedMetadataWriteAccess = false;
```

#### 8. Tags
```csharp
cosmosDbAccount.Tags.Add("Environment", "Production");
cosmosDbAccount.Tags.Add("Project", "MyApp");
cosmosDbAccount.Tags.Add("Owner", "TeamName");
```

## Complete Configuration Examples

### Example 1: Serverless Configuration with Custom Settings

```csharp
// In AppHost Program.cs
var cosmosDb = builder.AddAzureCosmosDB("cosmos-db")
    .RunAsEmulator() // Only for local development
    .ConfigureInfrastructure(infra =>
    {
        var cosmosDbAccount = infra.GetProvisionableResources()
                                   .OfType<CosmosDBAccount>()
                                   .Single();

        // Enable serverless mode
        cosmosDbAccount.Capabilities = new[]
        {
            new CosmosDBCapability { Name = "EnableServerless" }
        };

        // Set consistency policy
        cosmosDbAccount.ConsistencyPolicy = new ConsistencyPolicy
        {
            DefaultConsistencyLevel = DefaultConsistencyLevel.Session
        };

        // Enable free tier (one per subscription)
        cosmosDbAccount.EnableFreeTier = true;

        // Add tags
        cosmosDbAccount.Tags.Add("Environment", "Development");
        cosmosDbAccount.Tags.Add("CostCenter", "Engineering");
    });
```

### Example 2: Multi-Region Provisioned Throughput

```csharp
var cosmosDb = builder.AddAzureCosmosDB("cosmos-db")
    .ConfigureInfrastructure(infra =>
    {
        var cosmosDbAccount = infra.GetProvisionableResources()
                                   .OfType<CosmosDBAccount>()
                                   .Single();

        // Multi-region configuration
        cosmosDbAccount.Locations = new[]
        {
            new Location
            {
                LocationName = "East US",
                FailoverPriority = 0,
                IsZoneRedundant = true
            },
            new Location
            {
                LocationName = "West Europe",
                FailoverPriority = 1,
                IsZoneRedundant = true
            }
        };

        // Enable multi-region writes
        cosmosDbAccount.EnableMultipleWriteLocations = true;

        // Strong consistency for global apps
        cosmosDbAccount.ConsistencyPolicy = new ConsistencyPolicy
        {
            DefaultConsistencyLevel = DefaultConsistencyLevel.Strong
        };

        // Automatic failover
        cosmosDbAccount.EnableAutomaticFailover = true;
    });
```

### Example 3: Production Configuration with Backup

```csharp
var cosmosDb = builder.AddAzureCosmosDB("cosmos-db")
    .ConfigureInfrastructure(infra =>
    {
        var cosmosDbAccount = infra.GetProvisionableResources()
                                   .OfType<CosmosDBAccount>()
                                   .Single();

        // Production settings
        cosmosDbAccount.ConsistencyPolicy = new ConsistencyPolicy
        {
            DefaultConsistencyLevel = DefaultConsistencyLevel.BoundedStaleness,
            MaxIntervalInSeconds = 300,
            MaxStalenessPrefix = 100000
        };

        // Configure backup policy
        cosmosDbAccount.BackupPolicy = new PeriodicModeBackupPolicy
        {
            PeriodicModeProperties = new PeriodicModeProperties
            {
                BackupIntervalInMinutes = 240, // 4 hours
                BackupRetentionIntervalInHours = 720, // 30 days
                BackupStorageRedundancy = BackupStorageRedundancy.Geo
            }
        };

        // Security settings
        cosmosDbAccount.DisableKeyBasedMetadataWriteAccess = true;
        cosmosDbAccount.IsVirtualNetworkFilterEnabled = true;

        // Tags for cost management
        cosmosDbAccount.Tags.Add("Environment", "Production");
        cosmosDbAccount.Tags.Add("Application", "MortgageApp");
        cosmosDbAccount.Tags.Add("Owner", "DevTeam");
    });
```

### Example 4: Connect to Existing Cosmos DB Account

```csharp
// Instead of creating a new account, connect to existing one
var cosmosDb = builder.AddConnectionString("cosmos-db");

// Or specify the connection string directly
var cosmosDb = builder.AddConnectionString("cosmos-db", 
    "AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=your-key;");
```

## Parameter Configuration via Parameters

You can also use parameters to make configuration dynamic:

```csharp
// Define parameters
var region = builder.AddParameter("cosmos-region", "East US");
var consistencyLevel = builder.AddParameter("cosmos-consistency", "Session");
var enableFreeTier = builder.AddParameter("cosmos-free-tier", "true");

var cosmosDb = builder.AddAzureCosmosDB("cosmos-db")
    .ConfigureInfrastructure(infra =>
    {
        var cosmosDbAccount = infra.GetProvisionableResources()
                                   .OfType<CosmosDBAccount>()
                                   .Single();

        // Use parameters
        cosmosDbAccount.Locations = new[]
        {
            new Location
            {
                LocationName = region.AsProvisioningParameter(infra),
                FailoverPriority = 0
            }
        };

        cosmosDbAccount.EnableFreeTier = enableFreeTier.AsProvisioningParameter(infra);
    });
```

## Important Notes

### Capacity Mode Limitations

- **Serverless**: 
  - Maximum 5,000 RU/s per container
  - No SLA for throughput/latency
  - Cannot use free tier discount
  - Cannot enable geo-redundancy or multi-region writes

- **Provisioned Throughput**:
  - Predictable performance
  - Can use autoscale or manual throughput
  - Supports all features including geo-redundancy

### Cost Considerations

1. **Free Tier**: 1,000 RU/s and 25 GB storage free (one per subscription)
2. **Serverless**: Pay per request (good for unpredictable workloads)
3. **Provisioned**: Pay for reserved capacity (good for predictable workloads)

### Deployment Process

1. **Local Development**: `dotnet run` or F5 in Visual Studio
   - Uses emulator if `.RunAsEmulator()` is configured
   - Otherwise provisions real Azure resources

2. **Azure Deployment**: `azd up`
   - Generates Bicep templates from your configuration
   - Provisions Azure resources
   - Deploys application to Azure Container Apps

## Best Practices

1. **Use Emulator Locally**: Always use `.RunAsEmulator()` for local development
2. **Configure for Production**: Use `.ConfigureInfrastructure()` to set production parameters
3. **Use Parameters**: Make configuration dynamic with parameters
4. **Tag Resources**: Add meaningful tags for cost management
5. **Choose Appropriate Consistency**: Balance consistency needs with performance
6. **Consider Geo-Distribution**: Add regions close to your users
7. **Monitor Costs**: Start with serverless or free tier for development

## Troubleshooting

### Common Issues

1. **Configuration Not Applied**: Ensure you're calling `.ConfigureInfrastructure()` correctly
2. **Emulator Not Working**: Check if Docker is running and emulator is started
3. **Deployment Failures**: Check Azure quota and naming constraints
4. **Cost Overruns**: Monitor RU/s consumption and consider serverless mode

### Debugging

- Use `azd show` to see generated Bicep templates
- Check Azure portal for resource configuration
- Use Aspire Dashboard to monitor resource status
- Review deployment logs in Azure

## Additional Resources

- [Azure Cosmos DB Documentation](https://docs.microsoft.com/azure/cosmos-db/)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Azure.Provisioning.CosmosDB API Reference](https://learn.microsoft.com/dotnet/api/azure.provisioning.cosmosdb)
- [Cosmos DB Pricing](https://azure.microsoft.com/pricing/details/cosmos-db/)
