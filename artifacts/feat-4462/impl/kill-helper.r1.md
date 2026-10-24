# kill-helper r1
Added `using System.ComponentModel;` and `internal static void TryKillProcessTree(Process, ILogger)` to PlaudCliClient.cs exactly per task context; added PlaudCliClientTimeoutTests.cs (3 tests). `dotnet test --filter PlaudCliClientTimeoutTests`: 3 passed. Helper not yet wired (next task: wire-registration).
