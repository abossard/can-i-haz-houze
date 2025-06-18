#!/bin/bash

# 🤖 Can I Haz Houze - OpenAI Connection Setup Script
# This script automatically retrieves OpenAI connection details from Azure after deployment
# and configures them for local development

set -e

echo ""
echo "🤖 Setting up OpenAI connection for local development..."
echo "=================================================="

# Load environment variables from azd
echo "📄 Loading azd environment variables..."
eval "$(azd env get-values)"

# Check if we have the required environment variables
if [ -z "$AZURE_RESOURCE_GROUP" ]; then
    echo "❌ Error: AZURE_RESOURCE_GROUP not found in environment"
    echo "   Make sure you've run 'azd up' or 'azd provision' first"
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
    echo "❌ Error: Could not retrieve OpenAI endpoint"
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
    echo "❌ Error: Could not retrieve OpenAI API key"
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
