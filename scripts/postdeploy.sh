#!/bin/bash

# 🤖 Can I Haz Houze - Post Deployment Script
# This script runs after 'azd up' or 'azd deploy' to configure Azure resources and local development

set +e  # Don't exit on error - we want to continue even if some steps fail

echo ""
echo "🚀 Running post-deployment setup tasks..."
echo "=================================================="
echo ""

# Track overall success
OVERALL_SUCCESS=true

# Check if required tools are available
if ! command -v azd &> /dev/null; then
    echo "❌ Error: Azure Developer CLI (azd) is not installed"
    echo "   Please install it from: https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/install-azd"
    exit 1
fi

if ! command -v az &> /dev/null; then
    echo "❌ Error: Azure CLI (az) is not installed"
    echo "   Please install it from: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi

# Check if user is logged in to Azure
if ! az account show &> /dev/null; then
    echo "❌ Error: You are not logged in to Azure"
    echo "   Please run: az login"
    exit 1
fi

# Load environment variables from azd
echo "📄 Loading azd environment variables..."
eval "$(azd env get-values)"

# Debug: Show available environment variables (hide sensitive data)
echo "🔍 Available environment variables:"
azd env get-values | grep -E '^AZURE_|^SERVICE_' | sed 's/=.*/=***/' || echo "   (No Azure environment variables found)"
echo ""

# Find the resource group using multiple methods
AZURE_RESOURCE_GROUP=""

if [ -n "$AZURE_RESOURCE_GROUP" ]; then
    echo "✅ Found resource group from environment: $AZURE_RESOURCE_GROUP"
else
    echo "🔍 AZURE_RESOURCE_GROUP not found in environment, searching for resource group..."
    
    # Method 1: Look for resource groups with the env name
    if [ -n "$AZURE_ENV_NAME" ]; then
        echo "   Searching for resource group containing: $AZURE_ENV_NAME"
        AZURE_RESOURCE_GROUP=$(az group list --query "[?contains(name, '$AZURE_ENV_NAME')].name" --output tsv 2>/dev/null | head -1)
    fi
    
    # Method 2: Look for resource groups with 'can-i-haz-houze'
    if [ -z "$AZURE_RESOURCE_GROUP" ]; then
        echo "   Searching for resource group containing: can-i-haz-houze"
        AZURE_RESOURCE_GROUP=$(az group list --query "[?contains(name, 'can-i-haz-houze')].name" --output tsv 2>/dev/null | head -1)
    fi
    
    # Method 3: Look for any resource group with OpenAI resources
    if [ -z "$AZURE_RESOURCE_GROUP" ]; then
        echo "   Searching for any resource group with OpenAI resources..."
        for rg in $(az group list --query "[].name" --output tsv 2>/dev/null); do
            openai_count=$(az cognitiveservices account list --resource-group "$rg" --query "[?kind=='OpenAI'] | length(@)" --output tsv 2>/dev/null || echo "0")
            if [ "$openai_count" -gt 0 ]; then
                AZURE_RESOURCE_GROUP="$rg"
                echo "   Found OpenAI resource in: $rg"
                break
            fi
        done
    fi
fi

# Final check
if [ -z "$AZURE_RESOURCE_GROUP" ]; then
    echo "❌ Error: Could not find resource group"
    echo "   Available resource groups:"
    az group list --query "[].name" --output table 2>/dev/null || echo "   (Unable to list resource groups)"
    echo ""
    echo "   💡 Tip: Make sure you've run 'azd up' and the deployment completed successfully"
    exit 1
fi

echo "✅ Using resource group: $AZURE_RESOURCE_GROUP"
echo ""
echo "=================================================="
echo ""

# ===================================================================
# STEP 1: Enable Public Network Access
# ===================================================================
echo "📌 Step 1: Enabling public network access..."
echo ""

# Configure Storage Account
echo "🗄️  Configuring Storage Account..."
STORAGE_ACCOUNT=$(az storage account list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[0].name" \
    --output tsv 2>/dev/null)

if [ -n "$STORAGE_ACCOUNT" ]; then
    echo "   Found storage account: $STORAGE_ACCOUNT"
    
    if az storage account update \
        --name "$STORAGE_ACCOUNT" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --public-network-access Enabled \
        --allow-blob-public-access true \
        --output none 2>/dev/null; then
        echo "   ✅ Storage account configured successfully!"
    else
        echo "   ⚠️  Warning: Could not configure storage account"
        OVERALL_SUCCESS=false
    fi
else
    echo "   ℹ️  No storage account found"
fi

echo ""

# Configure Cosmos DB
echo "🌌 Configuring Cosmos DB..."
COSMOS_ACCOUNT=$(az cosmosdb list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[0].name" \
    --output tsv 2>/dev/null)

if [ -n "$COSMOS_ACCOUNT" ]; then
    echo "   Found Cosmos DB account: $COSMOS_ACCOUNT"
    
    if az cosmosdb update \
        --name "$COSMOS_ACCOUNT" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --public-network-access ENABLED \
        --output none 2>/dev/null; then
        echo "   ✅ Cosmos DB configured successfully!"
    else
        echo "   ⚠️  Warning: Could not configure Cosmos DB"
        OVERALL_SUCCESS=false
    fi
else
    echo "   ℹ️  No Cosmos DB account found"
fi

echo ""
echo "=================================================="
echo ""

# Configure Container Apps ingress settings needed for Blazor SignalR
echo "🌐 Configuring Container Apps ingress..."
WEBFRONTEND_APP=$(az containerapp list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[?contains(name, 'webfrontend')].name | [0]" \
    --output tsv 2>/dev/null)

if [ -n "$WEBFRONTEND_APP" ]; then
    echo "   Found web frontend app: $WEBFRONTEND_APP"

    if az containerapp ingress sticky-sessions set \
        --name "$WEBFRONTEND_APP" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --affinity sticky \
        --output none 2>/dev/null; then
        echo "   ✅ Sticky sessions enabled successfully!"
    else
        echo "   ⚠️  Warning: Could not enable sticky sessions"
        OVERALL_SUCCESS=false
    fi

    if az containerapp ingress update \
        --name "$WEBFRONTEND_APP" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --transport auto \
        --output none 2>/dev/null; then
        echo "   ✅ Ingress transport set to auto successfully!"
    else
        echo "   ⚠️  Warning: Could not set ingress transport to auto"
        OVERALL_SUCCESS=false
    fi
else
    echo "   ℹ️  No web frontend container app found"
fi

echo ""
echo "=================================================="
echo ""

# ===================================================================
# STEP 2: Setup Local OpenAI Connection
# ===================================================================
echo "📌 Step 2: Setting up local OpenAI connection..."
echo ""

# Find OpenAI resource
echo "🔍 Finding OpenAI resource..."
OPENAI_RESOURCE_NAME=$(az cognitiveservices account list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[?kind=='OpenAI'].name" \
    --output tsv 2>/dev/null | head -1)

if [ -z "$OPENAI_RESOURCE_NAME" ]; then
    echo "❌ Error: No OpenAI resource found in resource group '$AZURE_RESOURCE_GROUP'"
    echo "   Available resources:"
    az cognitiveservices account list --resource-group "$AZURE_RESOURCE_GROUP" --query "[].{Name:name, Kind:kind}" --output table 2>/dev/null
    OVERALL_SUCCESS=false
else
    echo "✅ Found OpenAI resource: $OPENAI_RESOURCE_NAME"
    
    # Get the endpoint
    echo "🔗 Retrieving OpenAI endpoint..."
    OPENAI_ENDPOINT=$(az cognitiveservices account show \
        --name "$OPENAI_RESOURCE_NAME" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --query "properties.endpoint" \
        --output tsv 2>/dev/null)
    
    if [ -z "$OPENAI_ENDPOINT" ]; then
        echo "❌ Error: Could not retrieve OpenAI endpoint"
        OVERALL_SUCCESS=false
    else
        echo "✅ Endpoint: $OPENAI_ENDPOINT"
        
        # Store in azd environment for Aspire
        echo "🌐 Updating azd environment parameters..."
        if azd env set existingOpenAIName "$OPENAI_RESOURCE_NAME" &> /dev/null && \
           azd env set existingOpenAIResourceGroup "$AZURE_RESOURCE_GROUP" &> /dev/null; then
            echo "✅ Stored OpenAI resource references in azd environment"
        else
            echo "⚠️  Warning: Failed to set azd environment parameters"
        fi
        
        # Create keyless connection string (no API key)
        CONNECTION_STRING="Endpoint=${OPENAI_ENDPOINT}"
        
        echo ""
        echo "🔐 Setting user secrets for all projects..."
        
        # Set user secrets for each project
        cd_success=true
        
        # AppHost
        if [ -f "./src/CanIHazHouze.AppHost/CanIHazHouze.AppHost.csproj" ]; then
            echo "  🏠 AppHost..."
            if (cd ./src/CanIHazHouze.AppHost && dotnet user-secrets set "ConnectionStrings:openai" "$CONNECTION_STRING" &> /dev/null); then
                echo "     ✅ Configured"
            else
                echo "     ⚠️  Failed"
                OVERALL_SUCCESS=false
            fi
        fi
        
        # AgentService
        if [ -f "./src/CanIHazHouze.AgentService/CanIHazHouze.AgentService.csproj" ]; then
            echo "  🤖 AgentService..."
            if (cd ./src/CanIHazHouze.AgentService && dotnet user-secrets set "ConnectionStrings:openai" "$CONNECTION_STRING" &> /dev/null); then
                echo "     ✅ Configured"
            else
                echo "     ⚠️  Failed"
                OVERALL_SUCCESS=false
            fi
        fi
        
        # DocumentService
        if [ -f "./src/CanIHazHouze.DocumentService/CanIHazHouze.DocumentService.csproj" ]; then
            echo "  📄 DocumentService..."
            if (cd ./src/CanIHazHouze.DocumentService && dotnet user-secrets set "ConnectionStrings:openai" "$CONNECTION_STRING" &> /dev/null); then
                echo "     ✅ Configured"
            else
                echo "     ⚠️  Failed"
                OVERALL_SUCCESS=false
            fi
        fi
        
        # Tests
        if [ -f "./src/CanIHazHouze.Tests/CanIHazHouze.Tests.csproj" ]; then
            echo "  🧪 Tests..."
            if (cd ./src/CanIHazHouze.Tests && dotnet user-secrets set "ConnectionStrings:openai" "$CONNECTION_STRING" &> /dev/null); then
                echo "     ✅ Configured"
            else
                echo "     ⚠️  Failed"
                OVERALL_SUCCESS=false
            fi
        fi
    fi
fi

echo ""
echo "=================================================="
echo ""

# Final summary
if [ "$OVERALL_SUCCESS" = true ]; then
    echo "🎉 All post-deployment tasks completed successfully!"
    echo ""
    echo "📋 Summary:"
    echo "   • Resource Group: $AZURE_RESOURCE_GROUP"
    [ -n "$STORAGE_ACCOUNT" ] && echo "   • Storage Account: $STORAGE_ACCOUNT (public access enabled)"
    [ -n "$COSMOS_ACCOUNT" ] && echo "   • Cosmos DB: $COSMOS_ACCOUNT (public access enabled)"
    [ -n "$OPENAI_RESOURCE_NAME" ] && echo "   • OpenAI: $OPENAI_RESOURCE_NAME"
    [ -n "$OPENAI_ENDPOINT" ] && echo "   • Endpoint: $OPENAI_ENDPOINT"
    echo "   • Auth Mode: Keyless (DefaultAzureCredential)"
    echo "   • User secrets configured for: AppHost, AgentService, DocumentService, Tests"
    echo ""
    echo "🚀 You can now run your app locally with:"
    echo "   cd src && dotnet run --project CanIHazHouze.AppHost"
    echo ""
    echo "🧪 Run tests with:"
    echo "   cd src && dotnet test"
    echo ""
    echo "💡 Note: Make sure you're logged in with 'az login' for local development"
else
    echo "⚠️  Some post-deployment tasks had issues, but deployment succeeded!"
    echo "   Review the output above for details."
    echo ""
    echo "📋 What was configured:"
    echo "   • Resource Group: $AZURE_RESOURCE_GROUP"
    [ -n "$STORAGE_ACCOUNT" ] && echo "   • Storage Account: $STORAGE_ACCOUNT"
    [ -n "$COSMOS_ACCOUNT" ] && echo "   • Cosmos DB: $COSMOS_ACCOUNT"
    [ -n "$OPENAI_RESOURCE_NAME" ] && echo "   • OpenAI: $OPENAI_RESOURCE_NAME"
    echo ""
    echo "🔧 To retry the setup, run:"
    echo "   azd hooks run postdeploy"
fi

echo "=================================================="
echo ""

# Always exit with 0 to not fail the deployment
exit 0
