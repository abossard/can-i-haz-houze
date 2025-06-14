# MortgageApprover Service

## Overview

The MortgageApprover service is a new microservice added to the CanIHazHouze application that handles mortgage request processing and approval workflows. This service follows the same patterns as the existing DocumentService and LedgerService.

## Features

### Core Functionality
- **Create Mortgage Requests**: Each user can have at most 1 mortgage request
- **Status Management**: Automatic status evaluation based on submitted data
- **Data Collection**: Extensible system for adding various types of mortgage data
- **CRUD Operations**: Full create, read, update, delete operations

### Request Statuses
- **Pending**: Initial state when request is created
- **RequiresAdditionalInfo**: Missing required documentation
- **UnderReview**: All documents received, manual review needed
- **Approved**: All criteria met, request approved
- **Rejected**: Criteria not met, request rejected

### Built-in Evaluation Logic
The service includes basic evaluation logic that assesses:
- Income verification
- Credit score (minimum 650)
- Employment verification  
- Property appraisal
- Debt-to-income ratio (maximum 43%)

## Architecture

### Technology Stack
- **.NET 9.0** - Runtime framework
- **ASP.NET Core** - Web API framework
- **Entity Framework Core** - Data access with SQLite
- **OpenAPI/Swagger** - API documentation
- **.NET Aspire** - Cloud-native application framework

### Database Design
The service uses SQLite with Entity Framework Core and includes:
- `MortgageRequest` entity with status tracking
- JSON storage for extensible request data
- Unique constraint ensuring one request per user

### API Endpoints

#### Mortgage Requests
- `POST /mortgage-requests` - Create new mortgage request
- `GET /mortgage-requests/{id}` - Get request by ID
- `GET /mortgage-requests/user/{userName}` - Get request by username
- `PUT /mortgage-requests/{id}/data` - Update request data
- `DELETE /mortgage-requests/{id}` - Delete request
- `GET /mortgage-requests` - List requests with pagination

#### Health Check
- `GET /health` - Service health status

## Integration

### .NET Aspire AppHost
The service is registered in the AppHost with:
- Health check monitoring
- Service discovery integration
- Automatic startup dependency management

### Web Frontend
The web application includes:
- `MortgageApiClient` for service communication
- Interactive Razor component at `/mortgage`
- Navigation menu integration

### Service Discovery
Uses .NET Aspire service discovery with the name `mortgageapprover`

## Configuration

### appsettings.json
```json
{
  "MortgageStorage": {
    "BaseDirectory": "MortgageData_Dev"
  }
}
```

### Development Setup
- Database: SQLite stored in `MortgageData_Dev/mortgage.db`
- Ports: HTTP 5295, HTTPS 7295
- Environment: Development configuration with detailed logging

## Usage Examples

### Creating a Request
```http
POST /mortgage-requests
Content-Type: application/json

{
  "userName": "john.doe"
}
```

### Adding Data
```http
PUT /mortgage-requests/{id}/data
Content-Type: application/json

{
  "data": {
    "income_verification": "verified",
    "annual_income": 75000,
    "credit_score": 720,
    "employment_verification": "verified",
    "property_appraisal": "completed",
    "loan_amount": 250000
  }
}
```

## Future Enhancements

The service is designed to be easily extensible for future requirements:

1. **Additional Data Fields**: The JSON storage approach allows for easy addition of new data types
2. **Complex Evaluation Rules**: The evaluation logic can be enhanced with more sophisticated criteria
3. **Document Integration**: Can integrate with the DocumentService for file uploads
4. **Workflow Management**: Can be extended with approval workflows and notifications
5. **Financial Integration**: Can connect with the LedgerService for payment processing

## Error Handling

The service includes comprehensive error handling:
- Input validation with appropriate HTTP status codes
- Logging of all operations and errors
- Graceful handling of database connection issues
- User-friendly error messages

## Security Considerations

- Input validation on all endpoints
- Parameterized database queries to prevent SQL injection
- Unique constraints to prevent data integrity issues
- Comprehensive logging for audit trails

## Testing

Test coverage includes:
- Unit tests for service logic
- Integration tests for API endpoints
- Database operation testing
- Error scenario validation

The service is ready for production deployment and can be easily extended as new requirements are identified.
