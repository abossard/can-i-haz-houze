# Partition Key Update Summary

## Overview
Updated all Cosmos DB container configurations and entity models to use `/owner` as the partition key instead of `/pk`. This ensures consistency across all services and proper partitioning based on the user/owner field. Also resolved JSON serialization conflicts in the MortgageApprover service.

## Changes Made

### 1. AppHost Configuration (`CanIHazHouze.AppHost/AppHost.cs`)
- Updated all container definitions to use `/owner` as the partition key:
  - `documents` container: `/owner`
  - `ledgers` container: `/owner` 
  - `mortgages` container: `/owner`

### 2. DocumentService (`CanIHazHouze.DocumentService/DocumentModels.cs`)
- **Entity Model Changes:**
  - Changed `DocumentEntity.pk` property to `DocumentEntity.owner`
  - Updated entity creation to set `owner = owner` instead of `pk = owner`
  
- **Query Changes:**
  - Updated query to use `c.owner = @owner` instead of `c.pk = @owner`

### 3. LedgerService (`CanIHazHouze.LedgerService/Program.cs`)
- **Entity Model Changes:**
  - Changed `AccountEntity.pk` property to `AccountEntity.owner`
  - Changed `TransactionEntity.pk` property to `TransactionEntity.owner`
  - Updated all entity creation statements to use `owner = owner` instead of `pk = owner`
  
- **Query Changes:**
  - Updated query to use `c.owner = @owner` instead of `c.pk = @owner`

### 4. MortgageApprover (`CanIHazHouze.MortgageApprover/Program.cs`)
- **Entity Model Changes:**
  - Changed `MortgageRequest.pk` property to `MortgageRequest.owner`
  - **JSON Serialization Fix:** Renamed `MortgageRequest.Id` to `MortgageRequest.RequestId` to avoid conflict with `id` property
  - Updated entity creation to set `owner = userName` instead of `pk = userName`
  - Added `System.Text.Json.Serialization` using directive
  - Updated all references from `.Id` to `.RequestId`
  
- **Query Changes:**
  - Updated queries to use `c.owner = @userName` instead of `c.pk = @userName`
  - Updated query to use `c.RequestId = @requestId` instead of `c.Id = @requestId`

## JSON Serialization Issue Resolution
**Problem:** The `MortgageRequest` class had both `id` (lowercase, for Cosmos DB) and `Id` (uppercase, for business logic) properties, causing System.Text.Json to throw a property name collision error.

**Solution:** Renamed the business logic property from `Id` to `RequestId` to follow the same pattern as other services:
- DocumentService: `id` + `DocumentId`
- LedgerService: `id` + `TransactionId`
- MortgageApprover: `id` + `RequestId` (fixed)

This approach avoids JSON serialization conflicts without requiring custom JsonPropertyName attributes.

## Partition Key Strategy
- **Partition Key Path:** `/owner`
- **Partition Key Values:** Username/owner identifier (string)
- **Benefits:**
  - All documents for a user are co-located in the same partition
  - Efficient queries when filtering by user/owner
  - Optimal RU consumption for user-specific operations
  - Consistent partition key strategy across all services

## Compatibility Notes
- **Cosmos DB Emulator:** Works with both Windows and Linux-based emulators
- **Apple Silicon:** Compatible with the new `vnext-preview` emulator image
- **Data Migration:** If existing data exists with the old `/pk` partition key, it would need to be migrated or recreated

## Verification
✅ All files compile without errors
✅ Entity models are consistent across all services
✅ Query syntax updated to match new property names
✅ AppHost container configurations updated
✅ Partition key strategy is uniform across all containers

## Next Steps
1. Test the application with the new partition key configuration
2. Verify that data operations work correctly across all services
3. If needed, migrate any existing data to use the new partition key structure
