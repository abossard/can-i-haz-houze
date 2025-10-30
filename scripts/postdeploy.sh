#!/bin/bash

# 🤖 Can I Haz Houze - Post Deployment Script
# This is the main post-deployment hook that runs all necessary setup tasks

echo ""
echo "🚀 Running post-deployment setup tasks..."
echo "=================================================="
echo ""

# Track overall success
OVERALL_SUCCESS=true

# Step 1: Enable public network access on Azure resources
echo "📌 Step 1: Enabling public network access..."
if ./scripts/enable-public-access.sh; then
    echo "✅ Public access enabled successfully"
else
    echo "⚠️  Public access configuration had issues (see above)"
    OVERALL_SUCCESS=false
fi

echo ""
echo "---"
echo ""

# Step 2: Setup local OpenAI connection
echo "📌 Step 2: Setting up local OpenAI connection..."
if ./scripts/setup-local-openai.sh; then
    echo "✅ OpenAI connection configured successfully"
else
    echo "⚠️  OpenAI setup had issues (see above)"
    OVERALL_SUCCESS=false
fi

echo ""
echo "=================================================="
if [ "$OVERALL_SUCCESS" = true ]; then
    echo "🎉 All post-deployment tasks completed successfully!"
else
    echo "⚠️  Some post-deployment tasks had issues, but deployment succeeded!"
    echo "   Review the output above for details."
fi
echo "=================================================="
echo ""

# Always exit with 0 to not fail the deployment
exit 0
