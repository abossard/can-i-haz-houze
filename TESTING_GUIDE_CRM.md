# CRM Service Testing Guide

## Overview
This guide provides instructions for testing the CRM (Customer Relationship Management) service for complaint management.

## Prerequisites
- Docker Desktop running
- .NET 9.0 SDK installed
- Azure OpenAI connection configured (for full app functionality)

## Running the Application

### Option 1: Run with Aspire Dashboard (Recommended)
```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

The Aspire dashboard will open at `https://localhost:17001` and show all services including:
- CRM Service
- Document Service
- Ledger Service
- Mortgage Approver
- Web Frontend
- Cosmos DB Emulator
- Azure Storage Emulator

### Option 2: Run CRM Service Standalone
```bash
cd src/CanIHazHouze.CrmService
dotnet run
```

## Test Scenarios

### 1. Create a Customer Complaint

**Web UI Test:**
1. Navigate to `https://localhost:{web-port}/complaints`
2. Enter customer name: `john_doe`
3. Click "Load Complaints"
4. Fill in the "Create New Complaint" form:
   - Title: "Delayed mortgage approval"
   - Description: "My mortgage approval process has been delayed for over 3 weeks without any updates"
5. Click "Create Complaint"
6. Verify complaint appears in the list with status "New"

**API Test:**
```bash
curl -X POST https://localhost:{crm-port}/complaints \
  -H "Content-Type: application/json" \
  -d '{
    "customerName": "john_doe",
    "title": "Delayed mortgage approval",
    "description": "My mortgage approval process has been delayed for over 3 weeks without any updates"
  }'
```

**Expected Result:**
- HTTP 201 Created
- Response contains complaint with generated ID and status "New"
- CreatedAt and UpdatedAt timestamps populated

### 2. View Customer Complaints

**Web UI Test:**
1. On Complaints page, ensure customer name is "john_doe"
2. Click "Load Complaints"
3. Verify list shows all complaints for john_doe
4. Verify complaint details (title, description, status, timestamps)

**API Test:**
```bash
curl https://localhost:{crm-port}/complaints?customerName=john_doe
```

**Expected Result:**
- HTTP 200 OK
- Array of complaints
- Each complaint has all fields populated

### 3. Update Complaint Status

**Web UI Test:**
1. Click "Show Details" on a complaint
2. In the "Update Status" section, click "In Progress"
3. Verify status badge changes to "In Progress" (yellow warning badge)
4. Verify "Updated" timestamp changes

**API Test:**
```bash
curl -X PUT https://localhost:{crm-port}/complaints/{id}/status?customerName=john_doe \
  -H "Content-Type: application/json" \
  -d '{"status": "InProgress"}'
```

**Expected Result:**
- HTTP 200 OK
- Complaint status updated to "InProgress"
- UpdatedAt timestamp updated

### 4. Add Support Comment

**Web UI Test:**
1. Click "Show Details" on a complaint
2. In "Add Comment" section:
   - Author name: "support_agent_1"
   - Comment text: "We have reviewed your case and are working on expediting the approval process"
3. Click "Add Comment"
4. Verify comment appears in the Comments section
5. Verify comment shows author, timestamp, and text

**API Test:**
```bash
curl -X POST https://localhost:{crm-port}/complaints/{id}/comments?customerName=john_doe \
  -H "Content-Type: application/json" \
  -d '{
    "authorName": "support_agent_1",
    "text": "We have reviewed your case and are working on expediting the approval process"
  }'
```

**Expected Result:**
- HTTP 200 OK
- Comment added to complaint's Comments list
- Comment has ID, author, text, and timestamp

### 5. Add Multiple Comments (Thread Test)

**Web UI Test:**
1. Add 3-4 comments from different authors:
   - "support_agent_1": Initial response
   - "john_doe": Follow-up question
   - "support_agent_2": Additional information
   - "manager_smith": Resolution update
2. Verify comments appear in chronological order (newest first)
3. Verify each comment shows correct author and timestamp

### 6. Add Approval Decision

**Web UI Test:**
1. Click "Show Details" on a complaint with status "Resolved"
2. In "Add Approval" section:
   - Approver name: "manager_smith"
   - Decision: "Approved"
   - Comments: "Complaint handled appropriately, customer compensation approved"
3. Click "Add Approval"
4. Verify approval appears with green "Approved" badge
5. Verify approval shows approver name, decision, and comments

**API Test:**
```bash
curl -X POST https://localhost:{crm-port}/complaints/{id}/approvals?customerName=john_doe \
  -H "Content-Type: application/json" \
  -d '{
    "approverName": "manager_smith",
    "decision": "Approved",
    "comments": "Complaint handled appropriately, customer compensation approved"
  }'
```

**Expected Result:**
- HTTP 200 OK
- Approval added to complaint's Approvals list
- Approval has ID, approver, decision, comments, and timestamp

### 7. Status Workflow Test

**Web UI Test:**
1. Create a new complaint (status: New)
2. Update status to "In Progress"
3. Add a comment: "Investigation in progress"
4. Update status to "Resolved"
5. Add approval: Approved
6. Update status to "Closed"
7. Verify each status change updates the badge color:
   - New: Blue
   - In Progress: Yellow
   - Resolved: Green
   - Closed: Gray

### 8. Delete Complaint

**Web UI Test:**
1. Click "Delete" button on a complaint
2. Verify complaint is removed from the list
3. Reload complaints to confirm deletion persisted

**API Test:**
```bash
curl -X DELETE https://localhost:{crm-port}/complaints/{id}?customerName=john_doe
```

**Expected Result:**
- HTTP 204 No Content
- Complaint no longer appears in list

### 9. Multiple Customers Test

**Web UI Test:**
1. Create complaints for different customers:
   - john_doe: 2 complaints
   - jane_smith: 3 complaints
   - bob_johnson: 1 complaint
2. Load complaints for each customer
3. Verify data isolation - each customer only sees their complaints

### 10. OpenAPI Documentation Test

1. Navigate to `https://localhost:{crm-port}/scalar/v1`
2. Verify all endpoints are documented:
   - POST /complaints
   - GET /complaints
   - GET /complaints/{id}
   - PUT /complaints/{id}/status
   - POST /complaints/{id}/comments
   - POST /complaints/{id}/approvals
   - DELETE /complaints/{id}
   - GET /health
3. Test API directly from Scalar UI
4. Verify request/response schemas are correct

## Test Data Creation Script

```bash
# Create multiple test complaints
for i in {1..5}; do
  curl -X POST https://localhost:{crm-port}/complaints \
    -H "Content-Type: application/json" \
    -d "{
      \"customerName\": \"john_doe\",
      \"title\": \"Test Complaint $i\",
      \"description\": \"This is test complaint number $i for testing purposes\"
    }"
done
```

## Performance Testing

### Load Test Scenario
1. Create 100 complaints using a loop
2. List all complaints
3. Update status on 50 complaints
4. Add comments to 75 complaints
5. Add approvals to 25 complaints
6. Delete 25 complaints
7. Monitor Cosmos DB RU consumption in Aspire dashboard

## Integration Testing with Other Services

### Document Service Integration (Future Enhancement)
Test scenario: Link complaint to related documents
1. Upload mortgage application document
2. Create complaint referencing document ID
3. Verify complaint can retrieve document reference

### Ledger Service Integration (Future Enhancement)
Test scenario: Link complaint to account
1. Create complaint with financial issue
2. Reference ledger account balance
3. Track compensation in ledger

## Expected Screenshots

When testing in Docker, capture screenshots of:

1. **Aspire Dashboard** - All services running (green status)
2. **CRM Service in Aspire** - Resource details and logs
3. **Complaints Page** - Empty state with "Enter customer name" message
4. **Complaints Page** - List of complaints for a customer
5. **Complaint Details** - Expanded view showing comments and approvals
6. **Create Complaint Form** - Filled in and ready to submit
7. **Status Update** - Showing status change from New to InProgress
8. **Comments Section** - Multiple comments from different authors
9. **Approvals Section** - Approval with Approved decision
10. **Scalar API Documentation** - Interactive API explorer
11. **Cosmos DB Data Explorer** - Showing CRM container with complaint documents

## Troubleshooting

### CRM Service Won't Start
- Check Docker is running
- Verify Cosmos DB emulator is running
- Check port conflicts
- Review logs in Aspire dashboard

### Cannot Create Complaints
- Verify customer name is provided
- Check title and description length limits (200 and 2000 chars)
- Review browser console for errors
- Check CRM service logs

### UI Not Showing Complaints
- Verify customer name matches exactly (case-sensitive)
- Check browser network tab for API call failures
- Ensure CRM service is running and healthy
- Verify service discovery is working (check AppHost logs)

## Success Criteria

All tests pass when:
- ✅ Complaints can be created, read, updated, and deleted
- ✅ Status workflow progresses correctly
- ✅ Comments are added and displayed chronologically
- ✅ Approvals are recorded with proper decisions
- ✅ Data is isolated per customer
- ✅ OpenAPI documentation is complete and accurate
- ✅ Web UI is responsive and user-friendly
- ✅ No security vulnerabilities detected
- ✅ All services integrate properly through Aspire
