# Azure AI Foundry Migration Summary

**Migration Date:** November 12, 2025  
**Status:** ✅ Complete

## Overview

Successfully migrated from Azure OpenAI to Azure AI Foundry across the Can I Haz Houze application. This migration modernizes the AI integration, provides better resource management, and prepares the application for multi-model scenarios.

## What Changed

### 1. AppHost (Orchestration Layer)

**Package Changes:**
- ❌ Removed: `Aspire.Hosting.Azure.CognitiveServices` v9.3.1
- ✅ Added: `Aspire.Hosting.Azure.AIFoundry` v9.3.1

**Resource Configuration:**
```csharp
// Before (Azure OpenAI):
var openaiAzure = builder.AddAzureOpenAI("openai");
openaiAzure.AddDeployment("gpt-4o", "gpt-4o", "2024-11-20");
openaiAzure.AddDeployment("gpt-4o-mini", "gpt-4o-mini", "2024-07-18");

// After (AI Foundry):
var foundry = builder.AddAzureAIFoundry("foundry");
var gpt4o = foundry.AddDeployment("gpt-4o", AIFoundryModel.OpenAI.Gpt4o);
var gpt4oMini = foundry.AddDeployment("gpt-4o-mini", AIFoundryModel.OpenAI.Gpt4oMini);

// References now point to specific deployments
documentService.WithReference(gpt4oMini);
agentService.WithReference(gpt4o);
```

**Key Benefits:**
- Deployments are now first-class resources with their own connection names
- Each service can reference a specific model deployment
- Better separation of concerns between document analysis (gpt-4o-mini) and agent execution (gpt-4o)

### 2. DocumentService

**Package Changes:**
- ❌ Removed: `Aspire.Azure.AI.OpenAI` v9.3.1-preview.1.25305.6
- ✅ Added: `Aspire.Azure.AI.Inference` v9.3.1

**Client Registration:**
```csharp
// Before:
builder.Services.AddSingleton(sp =>
{
    var credential = new Azure.Identity.DefaultAzureCredential();
    return new Azure.AI.OpenAI.AzureOpenAIClient(openAiUri, credential);
});

// After:
var connectionName = builder.Configuration.GetConnectionString("gpt-4o-mini") != null 
    ? "gpt-4o-mini"  // Production: AI Foundry deployment
    : "openai";       // Local dev: fallback connection string

builder.AddAzureChatCompletionsClient(connectionName);
```

**Service Implementation (DocumentAIService.cs):**
- Changed from `AzureOpenAIClient` to `ChatCompletionsClient`
- Updated API calls to use Azure.AI.Inference SDK patterns
- Simplified configuration (no more model deployment name in constructor)

**API Changes:**
```csharp
// Before:
var chatClient = _openAIClient.GetChatClient(_modelDeploymentName);
var response = await chatClient.CompleteChatAsync([...], options, cancellationToken);
var content = response.Value.Content[0].Text;

// After:
var requestOptions = new ChatCompletionsOptions { Messages = {...} };
var response = await _chatClient.CompleteAsync(requestOptions, cancellationToken);
var content = response.Value.Choices[0].Message.Content;
```

### 3. AgentService

**Package Changes:**
- ✅ Added: `Aspire.Azure.AI.Inference` v9.3.1
- ⚠️ Kept: `Azure.AI.OpenAI` v2.5.0-beta.1 (for Semantic Kernel compatibility)

**Dual Client Setup:**
```csharp
// Primary: AI Foundry client
var connectionName = builder.Configuration.GetConnectionString("gpt-4o") != null 
    ? "gpt-4o"     // Production: AI Foundry deployment
    : "openai";    // Local dev: fallback

builder.AddAzureChatCompletionsClient(connectionName);

// Legacy: Azure OpenAI client (for Semantic Kernel)
// Configured if connection string is available
```

**Updated Diagnostics:**
```csharp
// Enhanced diagnostics now show both clients
app.MapGet("/diagnostics/openai", (IServiceProvider sp) =>
{
    var azureClient = sp.GetService<Azure.AI.OpenAI.AzureOpenAIClient>();
    var inferenceClient = sp.GetService<Azure.AI.Inference.ChatCompletionsClient>();
    return Results.Ok(new
    {
        azureOpenAIConfigured = azureClient != null,
        aiFoundryConfigured = inferenceClient != null,
        mode = inferenceClient != null ? "AI Foundry" : (azureClient != null ? "Azure OpenAI" : "None")
    });
});
```

## Deployment Modes

### Production (Publish Mode)
When `azd up` provisions Azure resources:
1. Creates Azure AI Foundry resource named "foundry"
2. Deploys two models:
   - `gpt-4o` → used by AgentService
   - `gpt-4o-mini` → used by DocumentService
3. Connection strings injected automatically: `ConnectionStrings:gpt-4o` and `ConnectionStrings:gpt-4o-mini`

### Local Development
When running locally via `dotnet run`:
1. Reads `ConnectionStrings:openai` from user secrets (backward compatible)
2. Both services use the same connection string
3. No model separation (uses whatever is configured in the endpoint)

## Connection String Format

AI Foundry uses the same format as Azure OpenAI:
```
Endpoint=https://<resource-name>.openai.azure.com/;DeploymentId=<model-name>
```

For local dev (backward compatible):
```
Endpoint=https://<resource-name>.openai.azure.com/
```

## Benefits of Migration

### ✅ Immediate Benefits
1. **Modern SDK**: Using stable `Aspire.Azure.AI.Inference` instead of preview packages
2. **Better Separation**: Document analysis uses smaller/cheaper model (gpt-4o-mini)
3. **Simplified Configuration**: Aspire handles connection management automatically
4. **Future-Proof**: Easier to add more models or switch between them

### ✅ Long-term Benefits
1. **Multi-Model Support**: Foundation for using different models per workload
2. **Cost Optimization**: Each service can use the most cost-effective model
3. **Local Development Option**: Can use `RunAsFoundryLocal()` with Foundry Local emulator
4. **Role-Based Access**: Built-in support for `WithRoleAssignments()`

### ⚠️ Considerations
1. **Semantic Kernel**: AgentService still uses legacy Azure.AI.OpenAI for SK compatibility
2. **Breaking Change**: Connection string names changed in production (but handled gracefully)
3. **Testing Required**: Need to verify all AI features work with new client

## Testing Checklist

Before deploying to production, verify:

- [ ] DocumentService can analyze documents
- [ ] DocumentService can suggest tags
- [ ] DocumentService can generate summaries
- [ ] AgentService can execute agents
- [ ] AgentService can continue chat sessions
- [ ] Local development mode works (with `ConnectionStrings:openai`)
- [ ] Production deployment creates AI Foundry resource correctly
- [ ] Both models (gpt-4o, gpt-4o-mini) are deployed
- [ ] Role assignments work correctly

## Rollback Plan

If issues arise, revert with:
```bash
git revert <migration-commit>
cd src
dotnet restore
dotnet run --project CanIHazHouze.AppHost
```

Or manually:
1. Restore `Aspire.Hosting.Azure.CognitiveServices` in AppHost
2. Restore `Aspire.Azure.AI.OpenAI` in DocumentService
3. Revert AppHost.cs to use `AddAzureOpenAI()`
4. Revert service client registrations

## Next Steps

### Optional Enhancements
1. **Use IChatClient Interface**: Add `.AddChatClient()` for better abstraction
   ```csharp
   builder.AddAzureChatCompletionsClient("gpt-4o-mini")
          .AddChatClient();
   ```

2. **Local Development with Foundry Local**:
   ```csharp
   var foundry = builder.AddAzureAIFoundry("foundry")
                        .RunAsFoundryLocal();
   var model = AIFoundryModel.Local.Phi4Mini;
   ```

3. **Add More Models**: Easily add embeddings, vision, or other models
   ```csharp
   var embeddings = foundry.AddDeployment("embeddings", AIFoundryModel.OpenAI.TextEmbedding3Large);
   ```

4. **Configure SKU and Capacity**:
   ```csharp
   var chat = foundry.AddDeployment("chat", model)
                     .WithProperties(deployment =>
                     {
                         deployment.SkuName = "Standard";
                         deployment.SkuCapacity = 10;
                     });
   ```

## Documentation Updates Needed

- [ ] Update README.md with new AI Foundry setup instructions
- [ ] Update local development guide
- [ ] Update deployment guide (azd commands)
- [ ] Update API documentation if endpoint behavior changed

## References

- [Aspire Azure AI Foundry Integration Docs](https://learn.microsoft.com/en-us/dotnet/aspire/azureai/azureai-foundry-integration)
- [Azure AI Foundry Overview](https://ai.azure.com/)
- [Azure.AI.Inference SDK](https://learn.microsoft.com/en-us/dotnet/api/azure.ai.inference)
