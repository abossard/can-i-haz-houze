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

// Add Azure OpenAI for document processing
// For local development, you can use a connection string to an existing OpenAI service
// For production, this will provision a new Azure OpenAI resource
var openai = builder.ExecutionContext.IsPublishMode
    ? builder.AddAzureOpenAI("openai")
    : builder.AddConnectionString("openai");

// Configure OpenAI resource for production
if (builder.ExecutionContext.IsPublishMode)
{
    // Add model deployments for document processing and agent execution
    var openaiResource = (IResourceBuilder<AzureOpenAIResource>)openai;
    
    // GPT-5 Series - Advanced reasoning models
    // GPT-5 - Flagship reasoning model for logic-heavy tasks
    openaiResource.AddDeployment(
        name: "gpt-5",
        modelName: "gpt-5", 
        modelVersion: "2024-11-01");
    
    // GPT-5 Mini - Lightweight reasoning model
    openaiResource.AddDeployment(
        name: "gpt-5-mini",
        modelName: "gpt-5-mini", 
        modelVersion: "2024-11-01");
    
    // GPT-5 Nano - Speed and low latency reasoning
    openaiResource.AddDeployment(
        name: "gpt-5-nano",
        modelName: "gpt-5-nano", 
        modelVersion: "2024-11-01");
    
    // GPT-4.1 Series - Fast-response models
    // GPT-4.1 - Fast response with 1M context
    openaiResource.AddDeployment(
        name: "gpt-41",
        modelName: "gpt-4.1", 
        modelVersion: "2024-11-01");
    
    // GPT-4.1 Mini - Balanced performance and cost
    openaiResource.AddDeployment(
        name: "gpt-41-mini",
        modelName: "gpt-4.1-mini", 
        modelVersion: "2024-11-01");
    
    // GPT-4.1 Nano - Lowest cost and latency
    openaiResource.AddDeployment(
        name: "gpt-41-nano",
        modelName: "gpt-4.1-nano", 
        modelVersion: "2024-11-01");
}

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
// Single container for all agent-related entities with agentId as partition key
var agentsContainer = houzeDatabase.AddContainer("agents", "/agentId");

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithReference(openai) // Add OpenAI reference for document processing
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
    .WithReference(cosmos) // Reference the cosmos resource
    .WithReference(openai) // Add OpenAI reference for agent execution
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.CanIHazHouze_Web>("webfrontend")
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

builder.Build().Run();
