# CanIHazHouze CRM Service

Customer Relationship Management service for handling customer complaints with status tracking, comments, and approval workflows.

## Features

- **Complaint Management**: Create, read, update, and delete customer complaints
- **Status Tracking**: Track complaints through their lifecycle (New → InProgress → Resolved → Closed)
- **Comments**: Add support comments to complaints for communication tracking
- **Approvals**: Record approval/rejection decisions with optional comments
- **OpenAPI/Scalar Documentation**: Interactive API documentation at `/scalar/v1`

## API Endpoints

### Complaint Operations
- `POST /complaints` - Create a new complaint
- `GET /complaints?customerName={name}` - List all complaints for a customer
- `GET /complaints/{id}?customerName={name}` - Get a specific complaint
- `PUT /complaints/{id}/status?customerName={name}` - Update complaint status
- `DELETE /complaints/{id}?customerName={name}` - Delete a complaint

### Comment Operations
- `POST /complaints/{id}/comments?customerName={name}` - Add a comment to a complaint

### Approval Operations
- `POST /complaints/{id}/approvals?customerName={name}` - Add an approval decision to a complaint

### Health Check
- `GET /health` - Service health check

## Data Models

### Complaint
```json
{
  "id": "guid",
  "customerName": "string",
  "title": "string",
  "description": "string",
  "status": "New|InProgress|Resolved|Closed",
  "createdAt": "datetime",
  "updatedAt": "datetime",
  "comments": [],
  "approvals": []
}
```

### ComplaintComment
```json
{
  "id": "guid",
  "authorName": "string",
  "text": "string",
  "createdAt": "datetime"
}
```

### ComplaintApproval
```json
{
  "id": "guid",
  "approverName": "string",
  "decision": "Pending|Approved|Rejected",
  "comments": "string",
  "createdAt": "datetime"
}
```

## Storage

Uses Azure Cosmos DB with the following configuration:
- Database: `houze`
- Container: `crm`
- Partition Key: `/customerName`

## Running Locally

The service is part of the CanIHazHouze Aspire application. Run the entire application using:

```bash
cd src
dotnet run --project CanIHazHouze.AppHost
```

The CRM service will be automatically discovered and started by the AppHost.

## Testing

Access the OpenAPI documentation at:
- Scalar UI: `https://localhost:{port}/scalar/v1`
- OpenAPI JSON: `https://localhost:{port}/openapi/v1.json`

## Example Usage

### Create a Complaint
```bash
curl -X POST https://localhost:{port}/complaints \
  -H "Content-Type: application/json" \
  -d '{
    "customerName": "john_doe",
    "title": "Service delay issue",
    "description": "My mortgage approval has been delayed for 3 weeks"
  }'
```

### Add a Comment
```bash
curl -X POST https://localhost:{port}/complaints/{id}/comments?customerName=john_doe \
  -H "Content-Type: application/json" \
  -d '{
    "authorName": "support_agent",
    "text": "We are investigating your case"
  }'
```

### Add an Approval
```bash
curl -X POST https://localhost:{port}/complaints/{id}/approvals?customerName=john_doe \
  -H "Content-Type: application/json" \
  -d '{
    "approverName": "manager_smith",
    "decision": "Approved",
    "comments": "Approved for resolution"
  }'
```
