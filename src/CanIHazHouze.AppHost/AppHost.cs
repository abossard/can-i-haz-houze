var builder = DistributedApplication.CreateBuilder(args);

var documentService = builder.AddProject<Projects.CanIHazHouze_DocumentService>("documentservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.CanIHazHouze_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(documentService)
    .WaitFor(documentService);

builder.Build().Run();
