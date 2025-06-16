# Structured Input Forms Demo

The MortgageRequest.razor page now includes structured input forms for all mortgage requirements instead of simple "Add Sample Data" buttons.

## Available Forms

### 1. Income Verification Form
- **Annual Income** (number field): Enter annual salary in dollars
- **Employment Type** (dropdown): Full-time, Part-time, Contract, Self-employed
- **Years Employed** (number field): Years of employment (supports decimals)

### 2. Credit Report Form
- **Credit Score** (number field): Score between 300-850
- **Report Date** (date picker): Date of credit report
- **Outstanding Debts** (number field): Total outstanding debt in dollars

### 3. Employment Verification Form
- **Employer Name** (text field): Name of current employer
- **Job Title** (text field): Current job position
- **Monthly Salary** (number field): Monthly salary in dollars
- **Employment Verified** (checkbox): Whether employment has been verified

### 4. Property Appraisal Form
- **Property Value** (number field): Appraised value of property in dollars
- **Loan Amount** (number field): Requested loan amount in dollars
- **Property Type** (dropdown): Single Family, Condominium, Townhouse, Multi-family
- **Appraisal Date** (date picker): Date of property appraisal
- **Appraisal Completed** (checkbox): Whether appraisal has been completed

## Features

### Form Validation
- Each form includes basic validation (required fields, value ranges)
- Forms show error messages for invalid input
- Success messages confirm when data is submitted

### Data Submission
- Each form submits structured data to the backend API
- Data is stored with meaningful keys (e.g., `income_annual`, `credit_score`)
- Forms clear after successful submission

### Status Updates
- Mortgage request status is automatically updated after data submission
- Status evaluation happens server-side based on available data
- UI reflects current status and missing requirements

## Usage Flow

1. **Create Request**: Enter username and create a new mortgage request
2. **Select Request**: Choose an existing request from the list to edit
3. **Fill Forms**: Use structured forms to enter mortgage requirement data
4. **Monitor Status**: Watch as status changes from "Pending" to "UnderReview" or "RequiresAdditionalInfo"
5. **Complete Application**: Continue adding data until status reaches "Approved" or "Rejected"

## Data Structure

The forms generate structured data that is stored in the mortgage request's `RequestData` dictionary:

```json
{
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
```

This structured approach provides much more meaningful data than the previous simple button approach and allows for proper mortgage evaluation logic.
