# Tasks Domain

## Overview

The Tasks domain implements a comprehensive job and task management system with dual-layer architecture. It combines immediate task queuing for ad-hoc operations with recurring background job scheduling for automated processes. The domain provides task locking mechanisms, audit trails, and management interfaces for monitoring and controlling system operations.

## Domain Model

### Core Entities

#### ScheduledTask

The `ScheduledTask` entity manages immediate task queue operations with pessimistic locking.

**Key Attributes**
- **TaskType**: String identifier for task categorization
- **Data**: JSON payload containing task parameters
- **CompletedDate**: Completion timestamp (null when pending)
- **CompletedBy**: User identifier who completed the task
- **LockDate**: Pessimistic lock timestamp
- **Result**: Execution result data

**Audit Properties** (from AuditedAggregateRoot)
- Creation and modification tracking
- User attribution for all operations
- Comprehensive audit trail

#### RecurringJob

The `RecurringJob` entity manages background job scheduling integration.

**Key Attributes**
- **Id**: Job identifier matching Hangfire job names
- **Enabled**: Flag controlling job execution

## Task Management Architecture

### Dual-Layer Approach

**1. Scheduled Tasks Layer**
- Database-backed task queue
- Immediate execution model
- Manual or API-triggered
- Pessimistic locking for concurrency

**2. Recurring Jobs Layer**
- Hangfire-based background scheduling
- Cron expression scheduling
- Automatic execution
- Enable/disable controls

### Task Lifecycle

#### Scheduled Task Flow
```
Create → Queue → Lock → Execute → Complete → Audit
```

**State Management**
- **Pending**: CompletedDate is null
- **Locked**: LockDate set, prevents concurrent access
- **Completed**: CompletedDate set, task finished
- **Timeout**: Lock expires after 10 minutes

#### Recurring Job Flow
```
Configure → Schedule → Enable → Execute → Monitor
```

**Job States**
- **Disabled**: Job exists but won't execute
- **Enabled**: Active and scheduled for execution
- **Running**: Currently executing
- **Failed**: Last execution failed

## Application Services

### TaskAppService

Primary service managing scheduled task operations.

#### Core Operations

**Task Retrieval**
```csharp
GetExportTaskAsync(taskType) // Get and lock available task
```
- Automatic task locking (10-minute timeout)
- Single task retrieval to prevent conflicts
- Function key authentication required

**Task Completion**
```csharp
CompleteAsync(taskId, resultData) // Mark task as completed
```
- Validation prevents double completion
- Result data storage for audit
- User attribution tracking

**Task Management**
```csharp
CreateAsync(createDto) // Create new scheduled task
UpdateAsync(id, updateDto) // Modify existing task
DeleteAsync(id) // Remove task
```

#### Security Features

**Function Key Authentication**
- API endpoint protection with function keys
- Currently hardcoded (needs configuration improvement)
- External system integration support

### RecurringJobsAppService

Manages background job scheduling and monitoring.

#### Job Control Operations

**State Management**
```csharp
GetListAsync() // Retrieve all jobs with status
SetEnabledAsync(jobId, enabled) // Enable/disable jobs
```

**Integration Features**
- Hangfire metadata integration
- Next execution time calculation
- Cron expression display
- Job health monitoring

## Current Task Types

### EshopExport
Currently the only defined scheduled task type:
- Export operations for e-commerce platforms
- On-demand execution via API
- Result tracking and validation

### Extensibility
The system supports additional task types through:
- TaskType string identifier
- JSON data payload flexibility
- Polymorphic handling patterns

## Background Jobs

### Current Recurring Jobs

**Import Operations**
- `Import_Shoptet_yesterday_CZK`: Daily Shoptet imports (CZK)
- `Import_Shoptet_yesterday_EUR`: Daily Shoptet imports (EUR)
- `Import_Comgate_yesterday_CZK`: Daily Comgate imports (CZK)
- `Import_Comgate_yesterday_EUR`: Daily Comgate imports (EUR)

**Logistics Operations**
- `Print_Picking_morning`: Morning picking list generation
- `Print_Picking_noon`: Noon picking list generation
- `Finish_Transport`: Transport completion (every 5 minutes)

**Reporting Operations**
- `Controlling`: Daily report generation (1:00 AM)

### Job Implementation Pattern

```csharp
public class MyBackgroundJob : AsyncBackgroundJob<MyJobArgs>
{
    public override async Task ExecuteAsync(MyJobArgs args)
    {
        if (!await _jobsService.IsEnabled(JobName))
            return;
            
        // Execute business logic
        await _businessService.DoWork(args);
    }
}
```

## Locking Mechanism

### Pessimistic Locking Strategy

**Lock Acquisition**
- 10-minute timeout for task locks
- Prevents concurrent task execution
- Automatic lock expiration

**Concurrency Handling**
- Single task retrieval per type
- Lock validation before processing
- Cleanup of expired locks

### Race Condition Prevention

**Database-Level Protection**
- Atomic lock acquisition
- Transaction-based state changes
- Consistent read-modify-write operations

## Monitoring and Management

### Task Monitoring

**Audit Trail Features**
- Complete task lifecycle tracking
- User attribution for all operations
- Execution timing and results
- Error logging and debugging

**Query Capabilities**
- Filter by task type
- Status-based filtering
- Date range analysis
- User activity tracking

### Job Monitoring

**Hangfire Integration**
- Real-time job status
- Execution history
- Error tracking
- Performance metrics

**Management Interface**
- Enable/disable controls
- Next execution display
- Cron expression management
- Health status indicators

## Integration Points

### External System Integration

**API Access**
- Function key authentication
- RESTful task endpoints
- JSON data exchange
- Result callback mechanisms

### Internal Domain Integration

**Cross-Domain Operations**
- Invoice import tasks
- Bank statement processing
- Logistics operations
- Report generation

## Error Handling

### Task Error Management

**Error Classification**
- Execution errors
- Timeout errors
- Validation errors
- System errors

**Recovery Mechanisms**
- Automatic lock expiration
- Manual task retry
- Error result logging
- Administrative intervention

### Job Error Handling

**Hangfire Integration**
- Automatic retry policies
- Dead letter queue handling
- Error notification
- Manual intervention support

## Configuration

### Task Configuration
- Lock timeout settings
- Function key management
- Task type definitions
- Execution parameters

### Job Configuration
- Cron expression setup
- Enable/disable defaults
- Retry policies
- Notification settings

## Performance Considerations

### Optimization Strategies

**Database Optimization**
- Efficient task queries
- Index optimization
- Lock timeout management
- Result size limitations

**Background Processing**
- Hangfire queue optimization
- Resource usage monitoring
- Execution time limits
- Memory management

### Scalability Features

- Multiple worker support
- Distributed job processing
- Database connection pooling
- Resource isolation

## Security and Access Control

### Authentication
- Function key-based API access
- Role-based job management
- User attribution tracking
- Audit trail security

### Authorization
- Task creation permissions
- Job management privileges
- Administrative controls
- External system access

## Business Value

The Tasks domain provides essential operational capabilities:

1. **Automation**: Eliminates manual repetitive operations
2. **Reliability**: Ensures critical business processes execute consistently
3. **Monitoring**: Provides visibility into system operations
4. **Flexibility**: Supports both immediate and scheduled operations
5. **Scalability**: Handles increasing workload efficiently
6. **Auditability**: Complete tracking for compliance and debugging