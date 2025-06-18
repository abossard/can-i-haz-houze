var builder = DistributedApplication.CreateBuilder(args);

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
    // Add model deployments for document processing
    var openaiResource = (IResourceBuilder<AzureOpenAIResource>)openai;
    
    openaiResource.AddDeployment(
        name: "gpt-4o-mini",
        modelName: "gpt-4o-mini", 
        modelVersion: "2024-07-18"); // GPT-4o mini for metadata extraction
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

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithExternalHttpEndpoints()
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithReference(openai) // Add OpenAI reference for document processing
    .WithReference(blobStorage) // Add Blob Storage reference for document file storage
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataOwner) // Grant Storage Blob Data Owner role for blob tags management
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
