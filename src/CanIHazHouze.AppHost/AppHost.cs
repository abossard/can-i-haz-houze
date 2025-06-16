var builder = DistributedApplication.CreateBuilder(args);

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithHttpHealthCheck("/health");

var ledgerService = builder.AddProject<Projects.CanIHazHouze_LedgerService>("ledgerservice")
    .WithHttpHealthCheck("/health");

var mortgageService = builder.AddProject<Projects.CanIHazHouze_MortgageApprover>("mortgageapprover")
    .WithHttpHealthCheck("/health")
    .WithReference(documentService)     // ← Added this
    .WithReference(ledgerService)       // ← Added this
    .WaitFor(documentService)           // ← Added this
    .WaitFor(ledgerService);            // ← Added this

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
