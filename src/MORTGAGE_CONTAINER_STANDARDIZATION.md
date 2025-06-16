# Mortgage Container Standardization Summary

## Overview
Standardized all mortgage-related documents to use the consistent type discriminator `"mortgage"` instead of `"mortgage-request"`. This ensures all documents go to the single `mortgages` container with a consistent document type.

## Changes Made

### 1. Document Type Discriminator
- **Before**: `Type = "mortgage-request"`
- **After**: `Type = "mortgage"`

### 2. Database Configuration
- **Container**: `mortgages` (unchanged - already correct)
- **Partition Key**: `/owner` (already updated)
- **Document Type**: Now consistently `"mortgage"`

### 3. Query Updates
Updated all Cosmos DB queries to use the new document type:

**CreateMortgageRequestAsync:**
```sql
-- Before: WHERE c.owner = @userName AND c.Type = @type with @type = "mortgage-request"
-- After:  WHERE c.owner = @userName AND c.Type = @type with @type = "mortgage"
```

**GetMortgageRequestAsync:**
```sql
-- Before: WHERE c.RequestId = @requestId AND c.Type = @type with @type = "mortgage-request" 
-- After:  WHERE c.RequestId = @requestId AND c.Type = @type with @type = "mortgage"
```

**GetMortgageRequestByUserAsync:**
```sql
-- Before: WHERE c.owner = @userName AND c.Type = @type with @type = "mortgage-request"
-- After:  WHERE c.owner = @userName AND c.Type = @type with @type = "mortgage"
```

**GetMortgageRequestsAsync:**
```sql
-- Before: WHERE c.Type = @type with @type = "mortgage-request"
-- After:  WHERE c.Type = @type with @type = "mortgage"
```

### 4. API Endpoints (Unchanged)
The REST API endpoints remain as `/mortgage-requests/...` for consistency with HTTP conventions, while the underlying document type is now `"mortgage"`.

## Result
✅ All mortgage documents now consistently use the `"mortgage"` document type
✅ Documents are stored in the single `mortgages` container
✅ Partition key is `/owner` for all mortgage documents
✅ No compilation errors
✅ API endpoints maintain RESTful naming conventions

## Data Migration Note
If existing data exists with the old `"mortgage-request"` type, it would need to be migrated or the queries would need to handle both types temporarily during transition.
