var builder = DistributedApplication.CreateBuilder(args);

// Add Azure Cosmos DB with emulator for local development
// Using the new Linux-based emulator that supports Apple Silicon
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator(emulator =>
    {
        emulator.WithLifetime(ContainerLifetime.Persistent)
                .WithDataVolume() // Persist data across container restarts
                .WithImageTag("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview");
    });

// Create shared database with separate containers for each service
var houzeDatabase = cosmos.AddCosmosDatabase("houze");

// Add containers with username as partition key for optimal RU sharing
var documentsContainer = houzeDatabase.AddContainer("documents", "/owner");
var accountsContainer = houzeDatabase.AddContainer("accounts", "/owner"); 
var transactionsContainer = houzeDatabase.AddContainer("transactions", "/owner");
var mortgageContainer = houzeDatabase.AddContainer("mortgageRequests", "/userName");

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithHttpHealthCheck("/health");

var ledgerService = builder.AddProject<Projects.CanIHazHouze_LedgerService>("ledgerservice")
    .WithReference(cosmos) // Reference the cosmos resource instead of container
    .WithHttpHealthCheck("/health");

var mortgageService = builder.AddProject<Projects.CanIHazHouze_MortgageApprover>("mortgageapprover")
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
