# Local OpenAI Setup for Development

## Overview

Both `CanIHazHouze.AgentService` and `CanIHazHouze.DocumentService` require Azure OpenAI to function. The services use **keyless authentication** via `DefaultAzureCredential`, which means:

1. **No API keys** are stored in code or configuration
2. Your **Azure credentials** (from `az login`) are used automatically
3. Services will **fail to start** if OpenAI is not properly configured

## Prerequisites

1. **Azure CLI** installed and authenticated:
   ```bash
   az login
   ```

2. **Azure OpenAI resource** with appropriate role assignments:
   - Your Azure account needs `Cognitive Services OpenAI User` or `Cognitive Services OpenAI Contributor` role on the OpenAI resource

## Setup Steps

### 1. Set User Secrets

The OpenAI endpoint must be configured via user secrets (NOT in appsettings.json to avoid committing sensitive info).

**After deployment**, the `postdeploy` hook automatically configures this for you. To manually set it up:

```bash
# For AgentService
cd src/CanIHazHouze.AgentService
dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://openaiu7cggpq7qizqs.openai.azure.com/"

# For DocumentService
cd ../CanIHazHouze.DocumentService
dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://openaiu7cggpq7qizqs.openai.azure.com/"

# For Tests
cd ../CanIHazHouze.Tests
dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://openaiu7cggpq7qizqs.openai.azure.com/"
```

### 2. Verify Configuration

```bash
# Check AgentService secrets
cd src/CanIHazHouze.AgentService
dotnet user-secrets list

# Check DocumentService secrets
cd ../CanIHazHouze.DocumentService
dotnet user-secrets list

# Check Tests secrets
cd ../CanIHazHouze.Tests
dotnet user-secrets list
```

You should see:
```
ConnectionStrings:openai = Endpoint=https://openaiu7cggpq7qizqs.openai.azure.com/
```

### 3. Ensure Azure Authentication

Make sure you're logged in to Azure CLI:

```bash
az login
az account show
```

The `DefaultAzureCredential` will automatically use your Azure CLI credentials for authentication.

### 4. Deploy and Configure

When you deploy with `azd up`, the postdeploy hook automatically:
- Retrieves the OpenAI endpoint from Azure
- Configures user secrets for all projects (AppHost, AgentService, DocumentService, Tests)

To manually trigger the setup after deployment:
```bash
azd hooks run postdeploy
```

### 5. Run the Application

```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

The Aspire dashboard will start, and all services should initialize successfully.

### 6. Run Tests

Tests now require real Azure OpenAI configuration (no more dummy/stub services):

```bash
cd src
dotnet test
```

Tests will use the OpenAI endpoint configured in step 1. Make sure you have:
- Azure CLI authentication (`az login`)
- User secrets configured for the test project
- Sufficient Azure OpenAI quota for test execution

## Troubleshooting

### Service fails to start with "OpenAI connection string is required"

- Make sure you've set the user secret (step 1)
- Verify with `dotnet user-secrets list` in each service directory

### Service fails with authentication error

- Run `az login` to authenticate
- Ensure your Azure account has the correct role on the OpenAI resource
- Try `az account show` to verify you're logged into the correct subscription

### "Invalid OpenAI endpoint" error

- Ensure the endpoint URL is valid HTTPS
- Format must be: `Endpoint=https://YOUR-RESOURCE.openai.azure.com/`
- Do NOT include port numbers or paths

## Security Notes

- **Never commit** the OpenAI endpoint to appsettings.json or appsettings.Development.json
- User secrets are stored locally in your user profile directory
- In production (Azure deployment), use Managed Identity and Azure Key Vault
- The `setup-openai-secret.sh` script can be safely committed (it doesn't contain secrets)

## What Changed from Previous Version

Previously, the services and tests had "dummy" fallback implementations that would run without OpenAI configured. This has been **removed** for the following reasons:

1. **Production parity**: Local dev should match production behavior
2. **Clear errors**: Better to fail fast than run with fake data
3. **Real testing**: Integration tests now validate actual Azure services, not stubs
4. **Security**: No confusion about whether real AI is running

### Changes to Tests

- Removed dummy OpenAI configuration from `AgentServiceEndToEndTests`
- Tests now require real Azure OpenAI endpoint via user secrets
- Added `UserSecretsId` to test project for configuration
- Tests validate real Azure integration, not stub implementations
