#!/bin/bash

# 🤖 Can I Haz Houze - OpenAI Connection Setup Script
# This script automatically retrieves OpenAI connection details from Azure after deployment
# and configures them for local development

# Add fallback instructions in case of any issues
handle_error() {
    echo ""
    echo "⚠️  Automatic setup encountered an error, but deployment succeeded!"
    echo "=================================================="
    echo ""
    echo "You can manually set up the OpenAI connection by:"
    echo ""
    echo "1. Find your resource group:"
    echo "   az group list --query \"[?contains(name, 'can-i-haz-houze')].name\" -o tsv"
    echo ""
    echo "2. Find your OpenAI resource:"
    echo "   az cognitiveservices account list --resource-group <your-rg> --query \"[?kind=='OpenAI'].name\" -o tsv"
    echo ""
    echo "3. Get connection details:"
    echo "   az cognitiveservices account show --name <openai-name> --resource-group <your-rg> --query properties.endpoint -o tsv"
    echo "   az cognitiveservices account keys list --name <openai-name> --resource-group <your-rg> --query key1 -o tsv"
    echo ""
    echo "4. Set user secrets:"
    echo "   cd src/CanIHazHouze.AppHost"
    echo "   dotnet user-secrets set \"ConnectionStrings:openai\" \"Endpoint=<endpoint>;ApiKey=<key>\""
    echo ""
    echo "💡 Your deployment was successful - this is just a local development setup issue!"
    echo ""
    exit 1
}

trap 'handle_error' ERR

echo ""
echo "🤖 Setting up OpenAI connection for local development..."
echo "=================================================="

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

# Debug: Show available environment variables (remove sensitive data)
echo "🔍 Available environment variables:"
azd env get-values | grep -E '^AZURE_|^SERVICE_' | sed 's/=.*/=***/' || echo "   (No Azure environment variables found)"
echo ""

# Try to find the resource group using multiple methods
AZURE_RESOURCE_GROUP=""

# Method 1: Check if AZURE_RESOURCE_GROUP is already set
if [ -n "$AZURE_RESOURCE_GROUP" ]; then
    echo "✅ Found resource group from environment: $AZURE_RESOURCE_GROUP"
else
    echo "🔍 AZURE_RESOURCE_GROUP not found in environment, searching for resource group..."
    
    # Method 2: Look for resource groups with the app name
    if [ -n "$AZURE_ENV_NAME" ]; then
        echo "   Searching for resource group containing: $AZURE_ENV_NAME"
        AZURE_RESOURCE_GROUP=$(az group list --query "[?contains(name, '$AZURE_ENV_NAME')].name" --output tsv 2>/dev/null | head -1)
    fi
    
    # Method 3: Look for resource groups with 'can-i-haz-houze'
    if [ -z "$AZURE_RESOURCE_GROUP" ]; then
        echo "   Searching for resource group containing: can-i-haz-houze"
        AZURE_RESOURCE_GROUP=$(az group list --query "[?contains(name, 'can-i-haz-houze')].name" --output tsv 2>/dev/null | head -1)
    fi
    
    # Method 4: Look for any resource group with OpenAI resources
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
    echo "❌ Error: Could not find a resource group with OpenAI resources"
    echo "   Available resource groups:"
    az group list --query "[].name" --output table 2>/dev/null || echo "   (Unable to list resource groups)"
    echo ""
    echo "   💡 Tip: Make sure you've run 'azd up' or 'azd provision' and the deployment completed successfully"
    echo "   💡 Tip: Check that you're logged into the correct Azure subscription with 'az account show'"
    exit 1
fi

# Find OpenAI resource name (it's usually prefixed with the app name)
echo "🔍 Finding OpenAI resource in resource group: $AZURE_RESOURCE_GROUP"

OPENAI_RESOURCE_NAME=$(az cognitiveservices account list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[?kind=='OpenAI'].name" \
    --output tsv 2>/dev/null | head -1)

if [ -z "$OPENAI_RESOURCE_NAME" ]; then
    echo "❌ Error: No OpenAI resource found in resource group '$AZURE_RESOURCE_GROUP'"
    echo "   Available resources:"
    az cognitiveservices account list --resource-group "$AZURE_RESOURCE_GROUP" --query "[].{Name:name, Kind:kind}" --output table 2>/dev/null || echo "   (Unable to list resources)"
    exit 1
fi

echo "✅ Found OpenAI resource: $OPENAI_RESOURCE_NAME"

# Get the endpoint
echo "🔗 Retrieving OpenAI endpoint..."
OPENAI_ENDPOINT=$(az cognitiveservices account show \
    --name "$OPENAI_RESOURCE_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "properties.endpoint" \
    --output tsv 2>/dev/null)

if [ -z "$OPENAI_ENDPOINT" ]; then
    echo "❌ Error: Could not retrieve OpenAI endpoint for resource '$OPENAI_RESOURCE_NAME'"
    echo "   This might be due to:"
    echo "   • Insufficient permissions to read the resource"
    echo "   • The resource is still being provisioned"
    echo "   • The resource is in an error state"
    exit 1
fi

# Get the API key
echo "🔑 Retrieving OpenAI API key..."
OPENAI_KEY=$(az cognitiveservices account keys list \
    --name "$OPENAI_RESOURCE_NAME" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "key1" \
    --output tsv 2>/dev/null)

if [ -z "$OPENAI_KEY" ]; then
    echo "❌ Error: Could not retrieve OpenAI API key for resource '$OPENAI_RESOURCE_NAME'"
    echo "   This might be due to:"
    echo "   • Insufficient permissions to read keys (need Cognitive Services Contributor role)"
    echo "   • The resource is still being provisioned"
    echo "   • The resource is in an error state"
    exit 1
fi

# Construct the connection string
CONNECTION_STRING="Endpoint=${OPENAI_ENDPOINT};ApiKey=${OPENAI_KEY}"

echo "✅ OpenAI connection details retrieved successfully!"
echo ""
echo "🛠️  Setting up local development configuration..."

# Set the user secrets for the AppHost project
APPHOST_PROJECT="./src/CanIHazHouze.AppHost/CanIHazHouze.AppHost.csproj"

if [ -f "$APPHOST_PROJECT" ]; then
    echo "🔐 Setting user secrets for AppHost project..."
    cd ./src/CanIHazHouze.AppHost
    dotnet user-secrets set "ConnectionStrings:openai" "$CONNECTION_STRING"
    cd ../..
    echo "✅ User secrets configured successfully!"
else
    echo "⚠️  Warning: AppHost project not found at $APPHOST_PROJECT"
    echo "   You'll need to manually set the connection string:"
    echo "   dotnet user-secrets set \"ConnectionStrings:openai\" \"$CONNECTION_STRING\""
fi

echo ""
echo "🎉 Setup Complete!"
echo "=================================================="
echo ""
echo "Your OpenAI connection is now configured for local development!"
echo ""
echo "📋 Summary:"
echo "   • Resource: $OPENAI_RESOURCE_NAME"
echo "   • Endpoint: $OPENAI_ENDPOINT"
echo "   • Connection configured in: CanIHazHouze.AppHost user secrets"
echo ""
echo "🚀 You can now run your app locally with:"
echo "   cd src && dotnet run --project CanIHazHouze.AppHost"
echo ""
echo "🔧 If you need to update the connection later, run:"
echo "   azd hooks run postdeploy"
echo ""
