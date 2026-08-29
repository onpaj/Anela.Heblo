# Design: Fix silent swallow of "no runner registered" error in RunDqtHandler

## Component Design

### `RunDqtHandler` (modified)
`backend/src/Anela.Heblo.Application/Features/DataQuality/UseCases/RunDqt/RunDqtHandler.cs`

Responsibilities (unchanged): validate the incoming `RunDqtRequest`, persist a new `DqtRun` in `Running` state, and kick off the actual comparison work out-of-band via a fire-and-forget task so the HTTP request returns promptly.

New responsibility: confirm a runner capable of handling `request.TestType` exists **before** persisting the `DqtRun`, and guarantee the fire-and-forget task cannot terminate without recording a terminal state on any exception it encounters, including one thrown before the delegated `IDqtJobRunner.RunAsync` is reached.

Internal structure after the change (pseudocode, not final C#):

```
Handle(request, ct):
    if request.DateFrom > request.DateTo:
        return Fail(DqtInvalidDateRange)                       # unchanged

    try:
        using validationScope = _scopeFactory.CreateScope()
        runners = validationScope.ServiceProvider.GetServices<IDqtJobRunner>()
        if not runners.Any(r => r.CanHandle(request.TestType)):
            return Fail(DqtUnsupportedTestType)                # NEW — no DqtRun persisted

        run = DqtRun.Start(request.TestType, request.DateFrom, request.DateTo,
                            DqtTriggerType.Manual, _timeProvider.GetUtcNow().DateTime)
        await _repository.AddAsync(run, ct)
        await _repository.SaveChangesAsync(ct)

        _ = Task.Run(async () =>
            using taskScope = _scopeFactory.CreateScope()
            try:
                runner = taskScope.ServiceProvider.GetServices<IDqtJobRunner>()
                    .SingleOrDefault(r => r.CanHandle(request.TestType))
                    ?? throw InvalidOperationException(...)
                await runner.RunAsync(run.Id)
            catch (Exception ex):                                # NEW safety net
                _logger.LogError(ex, "...", run.Id, request.TestType)
                scopedRepo = taskScope.ServiceProvider.GetRequiredService<IDqtRunRepository>()
                scopedRun = await scopedRepo.GetByIdAsync(run.Id, CancellationToken.None)
                scopedRun?.Fail(ex.Message, _timeProvider.GetUtcNow().DateTime)
                await scopedRepo.SaveChangesAsync(CancellationToken.None)
        , CancellationToken.None)

        _logger.LogInformation(...)                             # unchanged
        return Success(run.Id)

    catch (Exception ex):
        _logger.LogError(ex, "Error starting DQT run")
        return Fail(ErrorCodes.Exception)                       # unchanged
```

No new classes, interfaces, or files. `DqtRun`, `IDqtJobRunner`, `IDqtRunRepository`, `DataQualityModule` DI registrations are all reused as-is.

## Data Schemas

No database schema changes. `DqtRun` (`Anela.Heblo.Domain.Features.DataQuality.DqtRun`) keeps its existing columns/shape (`Status`, `ErrorMessage`, `CompletedAt`, etc.) — this fix only changes *when* a row is created and adds one more code path that can transition an existing row to `Failed`.

### `RunDqtResponse` (API shape — unchanged, new value on an existing field)
```json
// Existing shape, unchanged:
{
  "dqtRunId": "guid | null",
  "success": "bool",
  "errorCode": "string | null"
}
```
Behavioral change only: a request for a `DqtTestType` with no registered `IDqtJobRunner` now returns
```json
{ "dqtRunId": null, "success": false, "errorCode": "DqtUnsupportedTestType" }
```
synchronously (HTTP 200), instead of `{ "dqtRunId": "<guid>", "success": true, "errorCode": null }` followed by a run that silently never leaves `Running`. This mirrors the existing `DqtInvalidDateRange` synchronous-rejection shape already returned by the same endpoint, so no frontend contract change is needed — the frontend already renders `success: false` / `errorCode` generically for this endpoint.
