# Recurring Jobs Management

## Overview

The Recurring Jobs Management feature allows administrators to view, enable/disable, and manually trigger Hangfire recurring background jobs through a web-based interface.

## Features

- **Job Listing**: View all registered recurring jobs with their metadata
- **Status Toggle**: Enable or disable recurring jobs
- **Manual Trigger**: Manually execute jobs on-demand
- **Job Metadata**: Display name, description, cron expression, last modified info

## Architecture

### Backend Components

**Domain Layer** (`Anela.Heblo.Domain/Features/BackgroundJobs/`):
- `IRecurringJobStatusChecker` - Interface for checking job enabled status
- `IRecurringJobTriggerService` - Interface for manually triggering jobs

**Application Layer** (`Anela.Heblo.Application/Features/BackgroundJobs/`):
- `RecurringJobStatusChecker` - Checks Hangfire recurring job status
- `RecurringJobTriggerService` - Triggers jobs using Hangfire's `IBackgroundJobClient`
- `GetRecurringJobsList` - Use case for retrieving job list
- `UpdateRecurringJobStatus` - Use case for enabling/disabling jobs
- `TriggerRecurringJob` - Use case for manual job execution

**API Layer** (`Anela.Heblo.API/Controllers/`):
- `RecurringJobsController` - REST API endpoints for job management

**Persistence Layer** (`Anela.Heblo.Persistence/`):
- Uses Hangfire's storage for job metadata and status

### Frontend Components

**Pages**:
- `RecurringJobsPage.tsx` - Main page displaying job list with actions

**Dialogs**:
- `ConfirmTriggerJobDialog.tsx` - Confirmation dialog for manual job triggering

**API Hooks**:
- `useRecurringJobsQuery` - Fetches list of recurring jobs
- `useUpdateRecurringJobStatusMutation` - Mutation for enabling/disabling jobs
- `useTriggerRecurringJobMutation` - Mutation for triggering jobs manually

## Usage

### Viewing Jobs

Navigate to `/recurring-jobs` page to view:
- Display name and description
- Cron expression schedule
- Last modified timestamp and user
- Current status (Enabled/Disabled)
- Action buttons

### Enabling/Disabling Jobs

Click the status toggle button to enable or disable a recurring job:
- **Enabled** (green): Job will execute according to its cron schedule
- **Disabled** (gray): Job will not execute automatically

### Manually Triggering Jobs

Jobs can be manually triggered on-demand via the "Run Now" button in the UI:

1. Navigate to `/recurring-jobs` page
2. Click "Run Now" button for desired job
3. Confirm the action in the dialog
   - If job is disabled, dialog will show warning
   - Confirmation is required for both enabled and disabled jobs
4. Job is immediately enqueued in Hangfire (fire-and-forget)
5. Job execution can be monitored in Hangfire dashboard

**API Endpoint:**
- `POST /api/recurringjobs/{jobName}/trigger`
- Response: `{ "success": true, "jobId": "hangfire-job-id" }` (202 Accepted)

**Behavior:**
- Enabled jobs: Trigger immediately
- Disabled jobs: Can still be triggered manually with confirmation
- Fire-and-forget: API returns immediately with job ID, job runs asynchronously
- Uses reflection to build type-safe expression tree for Hangfire enqueueing

## API Endpoints

### GET /api/recurringjobs
Retrieves list of all recurring jobs with metadata.

**Response:**
```json
{
  "jobs": [
    {
      "jobName": "CheckLowStockAlerts",
      "displayName": "Check Low Stock Alerts",
      "description": "Checks for products below minimum stock threshold",
      "cronExpression": "0 */6 * * *",
      "isEnabled": true,
      "lastModifiedAt": "2026-01-05T10:00:00Z",
      "lastModifiedBy": "admin@example.com"
    }
  ]
}
```

### PUT /api/recurringjobs/{jobName}/status
Updates the enabled/disabled status of a recurring job.

**Request Body:**
```json
{
  "isEnabled": true
}
```

**Response:**
```json
{
  "success": true,
  "jobName": "CheckLowStockAlerts",
  "isEnabled": true,
  "message": "Job status updated successfully"
}
```

### POST /api/recurringjobs/{jobName}/trigger
Manually triggers a recurring job to execute immediately.

**Response (202 Accepted):**
```json
{
  "success": true,
  "jobId": "hangfire-background-job-id-123",
  "message": "Job triggered successfully"
}
```

**Response (404 Not Found - job doesn't exist):**
```json
{
  "success": false,
  "errorCode": "JOB_NOT_FOUND"
}
```

## Implementation Details

### Job Triggering Mechanism

The manual trigger functionality uses Hangfire's `IBackgroundJobClient.Enqueue()` method with reflection-based expression tree construction:

1. **Job Discovery**: Finds job instance from DI container using `IRecurringJob` interface
2. **Status Check**: Verifies job is enabled (can be bypassed with `forceDisabled` parameter)
3. **Expression Building**: Constructs `Expression<Func<T, Task>>` using reflection
4. **Enqueueing**: Passes expression to `IBackgroundJobClient.Enqueue<T>()` for type-safe execution
5. **Fire-and-Forget**: Returns immediately with background job ID

**Key Code:**
```csharp
// Build expression tree: (T job) => job.ExecuteAsync(cancellationToken)
var parameter = Expression.Parameter(jobType, "job");
var methodCall = Expression.Call(parameter, executeMethod, ...);
var lambda = Expression.Lambda(methodCall, parameter);

// Enqueue using generic method
var jobId = backgroundJobClient.Enqueue<T>(lambda);
```

This approach ensures:
- **Type Safety**: Hangfire serializes type information correctly
- **Flexibility**: Works with any `IRecurringJob` implementation
- **Testability**: Can be mocked for unit testing

## Security

- All endpoints require authentication via `[Authorize]` attribute
- Only authenticated users can view, modify, or trigger jobs
- Job execution permissions inherit from Hangfire configuration

## Error Handling

- **Job Not Found**: Returns 404 with `JOB_NOT_FOUND` error code
- **Job Disabled**: Returns error unless `forceDisabled` parameter is provided
- **Hangfire Errors**: Logged and returned as 500 Internal Server Error

## Testing

### Backend Tests
- `RecurringJobTriggerServiceTests.cs` - Unit tests for trigger service
- `TriggerRecurringJobHandlerTests.cs` - Tests for MediatR handler
- `RecurringJobsControllerTriggerTests.cs` - Integration tests for API endpoint

### Frontend Tests
- Unit tests for React components (Jest + React Testing Library)
- E2E tests for user workflows (Playwright against staging environment)

## Future Enhancements

- Job execution history and logs
- Job performance metrics
- Scheduled one-time job execution
- Job dependency management
- Batch job triggering
