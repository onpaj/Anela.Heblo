## Module
Purchase

## Finding
`PurchaseOrder.AddLine()` contains a raw `Console.WriteLine` debug statement:

```csharp
// backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs:72
Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");
```

This is inside a domain entity — the purest layer in Clean Architecture. Domain entities must have zero I/O concerns and no dependencies on infrastructure (including the console). Logging belongs in application or infrastructure layers, injected as an abstraction, never as a hard-wired side effect inside a domain method.

## Why it matters
- Domain entities must remain infrastructure-free. Console I/O is an infrastructure concern.
- `Console.WriteLine` in a hot path (every `AddLine` call) adds synchronous I/O in a web request context.
- It produces noise in production logs (stdout is captured by the container runtime) with no structured fields.
- The comment `// Debug logging` confirms it was never intended to stay.

## Suggested fix
Delete line 72 entirely:
```csharp
// Remove this line:
Console.WriteLine($"Added line {line.Id} to purchase order {Id}. Total lines: {_lines.Count}");
```
If add-line telemetry is genuinely needed, add it at the handler level (`CreatePurchaseOrderHandler` or `UpdatePurchaseOrderHandler`) where a proper `ILogger` is already injected.

---
_Filed by daily arch-review routine on 2026-05-22._