### task: register-timewindowparser-in-analyticsmodule


Add `services.AddScoped<TimeWindowParser>()` to `AnalyticsModule`. `TimeProvider.System` is already registered as a singleton by the host infrastructure — no extra registration needed for `TimeProvider` itself.

**File to modify:**
`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`

Locate the block that registers `IMarginCalculator` and `IMonthlyBreakdownGenerator` (currently lines 47–48). Add the new registration immediately before those lines, keeping the existing comments intact.

**Current block:**
```csharp
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```

**Replace with:**
```csharp
        services.AddScoped<TimeWindowParser>();
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```

No additional `using` is needed — `TimeWindowParser` is in `Anela.Heblo.Application.Features.Analytics.Services`, which is already imported via line 3 (`using Anela.Heblo.Application.Features.Analytics.Services;`).

**Verify:**
```bash
cd /home/user/worktrees/feature-3334-Arch-Review-Analytics-Timewindowparser-Uses-Dateti/backend
dotnet build 2>&1 | grep "error" | head -10
```

Still expect the handler error. Module itself should be clean.

**Commit:**
```
git add backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
git commit -m "feat: register TimeWindowParser as scoped service in AnalyticsModule"
```

---

