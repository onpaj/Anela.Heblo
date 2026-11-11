## Summary

Since 2026-10-09 ~07:25 UTC (09:25 CEST) production `heblo` (rgHeblo, image `v3.161.0`) is down / flapping with 502/503. No deploy happened — root cause is a process-killing unhandled exception in the Plaud adapter.

## Root cause

`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` — `RunCliCoreAsync`:

```csharp
cts.CancelAfter(TimeSpan.FromSeconds(options.ProcessTimeoutSeconds));
...
cts.Token.Register(() => process.Kill(entireProcessTree: true));
```

1. Plaud CLI started hanging and hitting the 60 s timeout (`System.TimeoutException: Plaud CLI process exceeded 60 seconds timeout`).
2. The `CancelAfter` timer fires the registered callback on a thread-pool timer thread. When the process is already gone, `Process.Kill(entireProcessTree: true)` throws `InvalidOperationException: No process is associated with this object.`
3. The exception escapes a timer callback → **unhandled → whole .NET process dies** (container exit code 139).
4. `plaud-polling` runs every 5 min, so it repeats. Restart takes ~3 min, close to the 230 s startup-probe limit → some starts are killed by the probe and the site gets blocked → effectively continuous outage.

Stack (prod log, 2026-10-09T07:31:09Z, repeated at 07:56, 08:21, 08:39, 09:01, 09:41 UTC):

```
Unhandled exception. System.AggregateException: One or more errors occurred. (No process is associated with this object.)
 ---> System.InvalidOperationException: No process is associated with this object.
   at System.Diagnostics.Process.EnsureState(State state)
   at System.Diagnostics.Process.GetWaitState()
   at System.Diagnostics.Process.KillTree(List`1& exceptions)
   at System.Diagnostics.Process.Kill(Boolean entireProcessTree)
   at Anela.Heblo.Adapters.Plaud.PlaudCliClient.<>c__DisplayClass11_0.<RunCliCoreAsync>b__0() in .../PlaudCliClient.cs:line 150
   at System.Threading.CancellationTokenSource.Invoke(...)
   ...
   at System.Threading.TimerQueueTimer.Fire(Boolean isThreadPool)
```

## Fix

- Never let the cancellation callback throw: wrap `Kill` in `try/catch` (`InvalidOperationException`, `Win32Exception`, ignore when `process.HasExited`).
- Dispose the registration (`using var registration = cts.Token.Register(...)`) so it can't fire after the method returns.
- Unit test: timeout path with a process that already exited must not throw out of the callback.

## Also investigate

- Why the Plaud CLI started hanging ~07:25 UTC (Plaud API slowness? auth? — see #3118 for the previous auth-expiry incident).
- Startup takes ~3 min on B2 plan `spHeblo` (10 sites), close to the 230 s limit; DB pool exhaustion / Npgsql timeouts visible around the crashes. Consider `WEBSITES_CONTAINER_START_TIME_LIMIT=600` in the prod deploy workflow.

## Mitigation

Disable recurring job `plaud-polling` (`public."RecurringJobConfigurations"`, `Id='plaud-polling'`, `IsEnabled=false`) until the fix ships.
