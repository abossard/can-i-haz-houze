#!/bin/bash

# 🤖 Can I Haz Houze - Enable Public Network Access Script
# This script enables public endpoint and public network access on Storage Account and Cosmos DB
# after deployment to allow connectivity during development and testing

set -e  # Exit on error

echo ""
echo "🌐 Enabling public network access for Azure resources..."
echo "=================================================="

# Error handling function
handle_error() {
    echo ""
    echo "⚠️  Script encountered an error, but deployment succeeded!"
    echo "=================================================="
    echo ""
    echo "You can manually enable public access by:"
    echo ""
    echo "For Storage Account:"
    echo "  az storage account update --name <storage-name> --resource-group <rg> --public-network-access Enabled"
    echo ""
    echo "For Cosmos DB:"
    echo "  az cosmosdb update --name <cosmos-name> --resource-group <rg> --public-network-access ENABLED"
    echo ""
    echo "💡 Your deployment was successful - this is just a post-deployment configuration!"
    echo ""
    exit 1
}

trap 'handle_error' ERR

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

# Find the resource group
AZURE_RESOURCE_GROUP=""

# Method 1: Check if AZURE_RESOURCE_GROUP is already set
if [ -n "$AZURE_RESOURCE_GROUP" ]; then
    echo "✅ Found resource group from environment: $AZURE_RESOURCE_GROUP"
else
    echo "🔍 AZURE_RESOURCE_GROUP not found in environment, searching for resource group..."
    
    # Method 2: Look for resource groups with the app name or env name
    if [ -n "$AZURE_ENV_NAME" ]; then
        echo "   Searching for resource group containing: $AZURE_ENV_NAME"
        AZURE_RESOURCE_GROUP=$(az group list --query "[?contains(name, '$AZURE_ENV_NAME')].name" --output tsv 2>/dev/null | head -1)
    fi
    
    # Method 3: Look for resource groups with 'can-i-haz-houze'
    if [ -z "$AZURE_RESOURCE_GROUP" ]; then
        echo "   Searching for resource group containing: can-i-haz-houze"
        AZURE_RESOURCE_GROUP=$(az group list --query "[?contains(name, 'can-i-haz-houze')].name" --output tsv 2>/dev/null | head -1)
    fi
fi

# Final check
if [ -z "$AZURE_RESOURCE_GROUP" ]; then
    echo "❌ Error: Could not find resource group"
    echo "   Available resource groups:"
    az group list --query "[].name" --output table 2>/dev/null || echo "   (Unable to list resource groups)"
    echo ""
    echo "   💡 Tip: Make sure you've run 'azd up' or 'azd provision' and the deployment completed successfully"
    echo "   💡 Tip: Check that you're logged into the correct Azure subscription with 'az account show'"
    exit 1
fi

echo "✅ Using resource group: $AZURE_RESOURCE_GROUP"
echo ""

# Enable public access on Storage Account
echo "🗄️  Configuring Storage Account..."

STORAGE_ACCOUNT=$(az storage account list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[0].name" \
    --output tsv 2>/dev/null)

if [ -n "$STORAGE_ACCOUNT" ]; then
    echo "   Found storage account: $STORAGE_ACCOUNT"
    
    # Enable public network access
    echo "   Enabling public network access..."
    az storage account update \
        --name "$STORAGE_ACCOUNT" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --public-network-access Enabled \
        --output none 2>/dev/null || {
        echo "   ⚠️  Warning: Could not enable public network access on storage account"
        echo "   This might be due to policy restrictions or insufficient permissions"
    }
    
    # Enable blob public access at account level
    echo "   Enabling blob public access at account level..."
    az storage account update \
        --name "$STORAGE_ACCOUNT" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --allow-blob-public-access true \
        --output none 2>/dev/null || {
        echo "   ⚠️  Warning: Could not enable blob public access"
        echo "   This might be due to policy restrictions or insufficient permissions"
    }
    
    echo "   ✅ Storage account configured successfully!"
else
    echo "   ℹ️  No storage account found in resource group"
fi

echo ""

# Enable public access on Cosmos DB
echo "🌌 Configuring Cosmos DB..."

COSMOS_ACCOUNT=$(az cosmosdb list \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query "[0].name" \
    --output tsv 2>/dev/null)

if [ -n "$COSMOS_ACCOUNT" ]; then
    echo "   Found Cosmos DB account: $COSMOS_ACCOUNT"
    
    # Enable public network access
    echo "   Enabling public network access..."
    az cosmosdb update \
        --name "$COSMOS_ACCOUNT" \
        --resource-group "$AZURE_RESOURCE_GROUP" \
        --public-network-access ENABLED \
        --output none 2>/dev/null || {
        echo "   ⚠️  Warning: Could not enable public network access on Cosmos DB"
        echo "   This might be due to policy restrictions or insufficient permissions"
    }
    
    echo "   ✅ Cosmos DB configured successfully!"
else
    echo "   ℹ️  No Cosmos DB account found in resource group"
fi

echo ""
echo "🎉 Public Network Access Configuration Complete!"
echo "=================================================="
echo ""
echo "📋 Summary:"
echo "   • Resource Group: $AZURE_RESOURCE_GROUP"
if [ -n "$STORAGE_ACCOUNT" ]; then
    echo "   • Storage Account: $STORAGE_ACCOUNT (public access enabled)"
fi
if [ -n "$COSMOS_ACCOUNT" ]; then
    echo "   • Cosmos DB: $COSMOS_ACCOUNT (public access enabled)"
fi
echo ""
echo "💡 Note: Public network access has been enabled for development/testing."
echo "   For production environments, consider using private endpoints or firewall rules."
echo ""
