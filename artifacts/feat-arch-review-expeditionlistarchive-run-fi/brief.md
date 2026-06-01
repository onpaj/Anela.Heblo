## Module
ExpeditionListArchive

## Finding
`ExpeditionListArchiveController.cs:1,57–62` contains:

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;
…
[HttpPost("run-fix")]
public async Task<ActionResult<RunExpeditionListPrintFixResponse>> RunFix(CancellationToken cancellationToken)
{
    var request = new RunExpeditionListPrintFixRequest();
    var response = await _mediator.Send(request, cancellationToken);
    return Ok(response);
}
```

`RunExpeditionListPrintFixRequest` and `RunExpeditionListPrintFixResponse` belong to the `ExpeditionList` module (`Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/`). The Archive controller imports and dispatches a use case it does not own, creating a hard compile-time dependency of `ExpeditionListArchive` on `ExpeditionList` internals. The endpoint is also semantically misrouted — `/api/expedition-list-archive/run-fix` suggests it belongs to the archive, but it triggers a live print-fix operation defined entirely in the sibling module.

The same cross-module pull is present in `ExpeditionListArchiveModule.cs:1–2`, which imports `IPrintQueueSink` from `Application.Features.ExpeditionList.Services` — a service interface internal to `ExpeditionList` — and `PrintPickingListOptions` from `Application.Features.ExpeditionList`, both of which are not in any `Contracts/` folder signalling intended cross-module use.

## Why it matters
`ExpeditionListArchive` cannot be understood, tested, or evolved without touching `ExpeditionList`. The development guidelines prohibit direct cross-module references; the correct pattern is for the consumer to define a contract interface and the provider to implement it (`ILeafletKnowledgeSource` / `KnowledgeBaseLeafletSourceAdapter` is the documented example).

## Suggested fix
Move the `[HttpPost("run-fix")]` action to `ExpeditionListController` (or create one if it does not exist), where it is co-located with the use case it dispatches. No new abstraction is needed — just move the one action.

For `PrintPickingListOptions` and `IPrintQueueSink`, consider moving the blob-container configuration that the Archive module genuinely needs into a dedicated shared contract or into the Archive module's own options class, rather than importing from `ExpeditionList`.

---
_Filed by daily arch-review routine on 2026-05-26._