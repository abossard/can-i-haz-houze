var builder = DistributedApplication.CreateBuilder(args);

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithHttpHealthCheck("/health");

var ledgerService = builder.AddProject<Projects.CanIHazHouze_LedgerService>("ledgerservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.CanIHazHouze_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(documentService)
    .WithReference(ledgerService)
    .WaitFor(documentService)
    .WaitFor(ledgerService);

builder.Build().Run();
