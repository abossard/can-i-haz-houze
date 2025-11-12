using Aspire.Hosting.Azure;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);


var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env")
                    .WithAzdResourceNaming();
                    
// Add Azure Cosmos DB with emulator for local development
// Using the new Linux-based emulator that supports Apple Silicon
#pragma warning disable ASPIRECOSMOSDB001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(emulator =>
    {
        emulator.WithLifetime(ContainerLifetime.Persistent);
        emulator.WithDataVolume();
        emulator.WithDataExplorer();
    });
#pragma warning restore ASPIRECOSMOSDB001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


// Configure OpenAI resource for production
// (Deployments moved into publish-mode branch above.)

// Add Azure Storage with Azurite emulator for local development
// Automatically uses real Azure Storage in production
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite =>
    {
        azurite.WithLifetime(ContainerLifetime.Persistent);
        azurite.WithDataVolume();
    });

// Add Blob Storage for document file storage
var blobStorage = storage.AddBlobs("blobs");

// Create shared database with separate containers for each service
var houzeDatabase = cosmos.AddCosmosDatabase("houze");

// Add containers with username as partition key for optimal RU sharing
var documentsContainer = houzeDatabase.AddContainer("documents", "/owner");
var ledgersContainer = houzeDatabase.AddContainer("ledgers", "/owner"); 
var mortgagesContainer = houzeDatabase.AddContainer("mortgages", "/owner");
var crmContainer = houzeDatabase.AddContainer("crm", "/customerName");
var agentsContainer = houzeDatabase.AddContainer("agents", "/agentId");

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithReference(blobStorage) // Add Blob Storage reference for document file storage
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataOwner) // Grant Storage Blob Data Owner role for blob operations
    .WithHttpHealthCheck("/health");

var ledgerService = builder.AddProject<Projects.CanIHazHouze_LedgerService>("ledgerservice")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithHttpHealthCheck("/health");

var mortgageService = builder.AddProject<Projects.CanIHazHouze_MortgageApprover>("mortgageapprover")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithReference(documentService)     // ← Keep for cross-service verification
    .WithReference(ledgerService)       // ← Keep for cross-service verification
    .WithHttpHealthCheck("/health")
    .WaitFor(documentService)           
    .WaitFor(ledgerService);

var crmService = builder.AddProject<Projects.CanIHazHouze_CrmService>("crmservice")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) // Reference the cosmos resource
    .WithHttpHealthCheck("/health");

var agentService = builder.AddProject<Projects.CanIHazHouze_AgentService>("agentservice")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) 
    .WithReference(ledgerService)   // ← Add reference for MCP endpoint discovery
    .WithReference(crmService)      // ← Add reference for MCP endpoint discovery
    .WithReference(documentService) // ← Add reference for MCP endpoint discovery
    .WithHttpHealthCheck("/health");

var webFrontend = builder.AddProject<Projects.CanIHazHouze_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(documentService)
    .WithReference(ledgerService)
    .WithReference(mortgageService)
    .WithReference(crmService)
    .WithReference(agentService)
    .WaitFor(documentService)
    .WaitFor(ledgerService)
    .WaitFor(mortgageService)
    .WaitFor(crmService)
    .WaitFor(agentService);

// Configure Production environment for Azure Container Apps deployment
if (builder.ExecutionContext.IsPublishMode)
{
    webFrontend.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    documentService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    ledgerService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    mortgageService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    crmService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
    agentService.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
}


// AI Foundry handling:
//  - Publish mode: provision Azure AI Foundry and register model deployments.
//  - Local dev: use existing resource via connection string (set by setup-local-openai.sh).
if (builder.ExecutionContext.IsPublishMode)
{
    var foundry = builder.AddAzureAIFoundry("foundry");
    
    // Add model deployments for document processing and agent execution
    var gpt4o = foundry.AddDeployment("gpt-4o", AIFoundryModel.OpenAI.Gpt4o);
    var gpt4oMini = foundry.AddDeployment("gpt-4o-mini", AIFoundryModel.OpenAI.Gpt4oMini);

    // Configure references to specific model deployments
    documentService.WithReference(gpt4oMini);
    agentService.WithReference(gpt4o);
}
else
{
    // Local development: use connection string for backward compatibility
    var openai = builder.AddConnectionString("openai");
    documentService.WithReference(openai);
    agentService.WithReference(openai);
}
builder.Build().Run();
