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
The service includes comprehensive evaluation logic that assesses:

#### Required Data Categories
1. **Income Verification** - Annual income, employment type, years employed
2. **Credit Report** - Credit score, report date, outstanding debts  
3. **Employment Verification** - Employer details, job title, salary, verification status
4. **Property Appraisal** - Property value, loan amount, property type, appraisal completion

#### Approval Criteria
- **Credit Score**: Minimum 650 required
- **Debt-to-Income Ratio**: Maximum 43% (calculated using 30-year mortgage at 7% interest)
- **Data Completeness**: All four requirement categories must have data present

#### Structured Data Fields
The service supports the following structured data fields:

**Income Fields:**
- `income_annual` (decimal) - Annual income in dollars
- `income_employment_type` (string) - full-time, part-time, contract, self-employed
- `income_years_employed` (decimal) - Years of employment (supports decimals)

**Credit Fields:**
- `credit_score` (int) - Credit score (300-850 range)
- `credit_report_date` (string) - ISO date format
- `credit_outstanding_debts` (decimal) - Outstanding debts in dollars

**Employment Fields:**
- `employment_employer` (string) - Employer name
- `employment_job_title` (string) - Job title
- `employment_monthly_salary` (decimal) - Monthly salary in dollars
- `employment_verified` (bool) - Verification status

**Property Fields:**
- `property_value` (decimal) - Appraised property value in dollars
- `property_loan_amount` (decimal) - Requested loan amount
- `property_type` (string) - single-family, condo, townhouse, multi-family
- `property_appraisal_date` (string) - ISO date format
- `property_appraisal_completed` (bool) - Appraisal completion status

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

## Typed Data Models

The service provides strongly typed DTOs for validation and documentation:

### MortgageIncomeDataDto
```csharp
public class MortgageIncomeDataDto
{
    [Range(0, double.MaxValue)]
    public decimal AnnualIncome { get; set; }
    
    [RegularExpression("^(full-time|part-time|contract|self-employed)$")]
    public string EmploymentType { get; set; }
    
    [Range(0, 50)]
    public decimal YearsEmployed { get; set; }
}
```

### MortgageCreditDataDto
```csharp
public class MortgageCreditDataDto
{
    [Range(300, 850)]
    public int CreditScore { get; set; }
    
    public DateTime ReportDate { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal OutstandingDebts { get; set; }
}
```

### MortgageEmploymentDataDto
```csharp
public class MortgageEmploymentDataDto
{
    [StringLength(200)]
    public string EmployerName { get; set; }
    
    [StringLength(200)]
    public string JobTitle { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal MonthlySalary { get; set; }
    
    public bool IsVerified { get; set; }
}
```

### MortgagePropertyDataDto
```csharp
public class MortgagePropertyDataDto
{
    [Range(0, double.MaxValue)]
    public decimal PropertyValue { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal LoanAmount { get; set; }
    
    [RegularExpression("^(single-family|condo|townhouse|multi-family)$")]
    public string PropertyType { get; set; }
    
    public DateTime AppraisalDate { get; set; }
    
    public bool AppraisalCompleted { get; set; }
}
```

## Field Reference

### MortgageDataFields Class
The service provides a constants class for field key management:

```csharp
// Income fields
MortgageDataFields.Income.Annual = "income_annual"
MortgageDataFields.Income.EmploymentType = "income_employment_type"
MortgageDataFields.Income.YearsEmployed = "income_years_employed"

// Credit fields  
MortgageDataFields.Credit.Score = "credit_score"
MortgageDataFields.Credit.ReportDate = "credit_report_date"
MortgageDataFields.Credit.OutstandingDebts = "credit_outstanding_debts"

// Employment fields
MortgageDataFields.Employment.Employer = "employment_employer"
MortgageDataFields.Employment.JobTitle = "employment_job_title"
MortgageDataFields.Employment.MonthlySalary = "employment_monthly_salary"
MortgageDataFields.Employment.Verified = "employment_verified"

// Property fields
MortgageDataFields.Property.Value = "property_value"
MortgageDataFields.Property.LoanAmount = "property_loan_amount"
MortgageDataFields.Property.Type = "property_type"
MortgageDataFields.Property.AppraisalDate = "property_appraisal_date"
MortgageDataFields.Property.AppraisalCompleted = "property_appraisal_completed"
```

## Usage Examples

### Creating a Request
```http
POST /mortgage-requests
Content-Type: application/json

{
  "userName": "john.doe"
}
```

### Adding Structured Data
```http
PUT /mortgage-requests/{id}/data
Content-Type: application/json

{
  "data": {
    "income_annual": 75000,
    "income_employment_type": "full-time",
    "income_years_employed": 3.5,
    "credit_score": 720,
    "credit_report_date": "2024-01-15",
    "credit_outstanding_debts": 15000,
    "employment_employer": "ABC Corporation",
    "employment_job_title": "Software Developer",
    "employment_monthly_salary": 6250,
    "employment_verified": true,
    "property_value": 325000,
    "property_loan_amount": 250000,
    "property_type": "single-family",
    "property_appraisal_date": "2024-01-10",
    "property_appraisal_completed": true
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
