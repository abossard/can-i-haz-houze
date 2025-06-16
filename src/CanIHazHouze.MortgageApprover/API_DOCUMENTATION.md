# MortgageApprover Service API Documentation

## Overview

The MortgageApprover service manages mortgage applications with structured data collection and automated approval logic. Each user can have only one mortgage request, and the system evaluates application status based on provided documentation and financial data.

## Data Structure

### Core Entity: MortgageRequest

```csharp
public class MortgageRequest
{
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public MortgageRequestStatus Status { get; set; }
    public string StatusReason { get; set; }
    public string MissingRequirements { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Dictionary<string, object> RequestData { get; set; } // Flexible data storage
}
```

### Status Enumeration

```csharp
public enum MortgageRequestStatus
{
    Pending,                    // Initial state
    UnderReview,               // All docs received, manual review needed
    Approved,                  // Automated approval criteria met
    Rejected,                  // Failed automated approval criteria
    RequiresAdditionalInfo     // Missing required documentation
}
```

## Structured Data Fields

The `RequestData` dictionary stores structured mortgage requirement data using the following field specifications:

### Income Verification Fields

| Field Key | Type | Description | Validation |
|-----------|------|-------------|------------|
| `income_annual` | decimal | Annual income in dollars | Must be > 0 |
| `income_employment_type` | string | Employment type | One of: full-time, part-time, contract, self-employed |
| `income_years_employed` | decimal | Years of employment | 0-50, supports decimals |

### Credit Report Fields

| Field Key | Type | Description | Validation |
|-----------|------|-------------|------------|
| `credit_score` | int | Credit score | 300-850 range |
| `credit_report_date` | string | Date of credit report | ISO date format (YYYY-MM-DD) |
| `credit_outstanding_debts` | decimal | Outstanding debts in dollars | Must be >= 0 |

### Employment Verification Fields

| Field Key | Type | Description | Validation |
|-----------|------|-------------|------------|
| `employment_employer` | string | Name of current employer | Required, max 200 chars |
| `employment_job_title` | string | Current job title | Required, max 200 chars |
| `employment_monthly_salary` | decimal | Monthly salary in dollars | Must be > 0 |
| `employment_verified` | bool | Whether employment verified | Boolean true/false |

### Property Appraisal Fields

| Field Key | Type | Description | Validation |
|-----------|------|-------------|------------|
| `property_value` | decimal | Appraised property value | Must be > 0 |
| `property_loan_amount` | decimal | Requested loan amount | Must be > 0, <= property_value |
| `property_type` | string | Type of property | One of: single-family, condo, townhouse, multi-family |
| `property_appraisal_date` | string | Date of property appraisal | ISO date format (YYYY-MM-DD) |
| `property_appraisal_completed` | bool | Whether appraisal completed | Boolean true/false |

## Approval Logic

### Requirements Check

All four requirement categories must have data present:
1. **Income Verification**: `income_annual` field present
2. **Credit Report**: `credit_score` field present  
3. **Property Appraisal**: `property_value` field present
4. **Employment Verification**: `employment_employer` field present

### Automated Approval Criteria

When all requirements are met, the system performs automated evaluation:

1. **Credit Score**: Must be >= 650
2. **Debt-to-Income Ratio**: Must be <= 43%
   - Monthly payment calculated using 30-year mortgage at 7% interest
   - DTI = Monthly Payment / Monthly Income
   - Monthly Income = Annual Income / 12

### Status Transitions

```
Pending → RequiresAdditionalInfo → UnderReview → Approved/Rejected
```

- **Pending**: Initial state when request is created
- **RequiresAdditionalInfo**: Missing one or more requirement categories
- **UnderReview**: All requirements present but insufficient data for auto-approval
- **Approved**: Meets all automated approval criteria
- **Rejected**: Fails credit score or DTI ratio requirements

## API Endpoints

### POST /mortgage-requests
Create a new mortgage request

**Request Body:**
```json
{
  "userName": "string"
}
```

**Response:** 201 Created with MortgageRequest object

### GET /mortgage-requests/{requestId}
Get mortgage request by ID

**Response:** 200 OK with MortgageRequest object

### GET /mortgage-requests/user/{userName}
Get mortgage request by username

**Response:** 200 OK with MortgageRequest object

### PUT /mortgage-requests/{requestId}/data
Update mortgage request data

**Request Body:**
```json
{
  "data": {
    "income_annual": 75000,
    "credit_score": 720,
    "property_value": 325000,
    "property_loan_amount": 250000
  }
}
```

**Response:** 200 OK with updated MortgageRequest object

### DELETE /mortgage-requests/{requestId}
Delete mortgage request

**Response:** 204 No Content

### GET /mortgage-requests
Get paginated list of mortgage requests

**Query Parameters:**
- `page`: Page number (default: 1)
- `pageSize`: Items per page (default: 10)
- `status`: Filter by status (optional)

**Response:** 200 OK with array of MortgageRequest objects

## Example Data Flow

### 1. Create Request
```json
POST /mortgage-requests
{
  "userName": "john.doe"
}
```

**Result:** Status = Pending, Missing all requirements

### 2. Add Income Data
```json
PUT /mortgage-requests/{id}/data
{
  "data": {
    "income_annual": 75000,
    "income_employment_type": "full-time",
    "income_years_employed": 3.5
  }
}
```

**Result:** Status = RequiresAdditionalInfo, Missing: Credit report, Property appraisal, Employment verification

### 3. Add Credit Data
```json
PUT /mortgage-requests/{id}/data
{
  "data": {
    "credit_score": 720,
    "credit_report_date": "2024-01-15",
    "credit_outstanding_debts": 15000
  }
}
```

**Result:** Status = RequiresAdditionalInfo, Missing: Property appraisal, Employment verification

### 4. Add Employment Data
```json
PUT /mortgage-requests/{id}/data
{
  "data": {
    "employment_employer": "ABC Corporation",
    "employment_job_title": "Software Developer",
    "employment_monthly_salary": 6250,
    "employment_verified": true
  }
}
```

**Result:** Status = RequiresAdditionalInfo, Missing: Property appraisal

### 5. Add Property Data
```json
PUT /mortgage-requests/{id}/data
{
  "data": {
    "property_value": 325000,
    "property_loan_amount": 250000,
    "property_type": "single-family",
    "property_appraisal_date": "2024-01-10",
    "property_appraisal_completed": true
  }
}
```

**Result:** Status = Approved (Credit score 720 >= 650, DTI ratio ~32% <= 43%)

## Legacy Field Support

The system maintains backward compatibility with legacy field names:

| Legacy Key | New Key | Description |
|------------|---------|-------------|
| `income_verification` | `income_annual` | Income verification flag |
| `credit_report` | `credit_score` | Credit report flag |
| `property_appraisal` | `property_value` | Property appraisal flag |
| `employment_verification` | `employment_employer` | Employment verification flag |
| `annual_income` | `income_annual` | Annual income amount |
| `loan_amount` | `property_loan_amount` | Loan amount |

## Error Handling

The API returns appropriate HTTP status codes:

- **400 Bad Request**: Invalid input data or validation errors
- **404 Not Found**: Requested resource doesn't exist
- **409 Conflict**: User already has an existing mortgage request
- **500 Internal Server Error**: Unexpected server errors

All errors include a descriptive message in the response body.
