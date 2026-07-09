### Question 1
**Abstraction choice**: This spec extends the existing `ITemporaryFileAccessor` (Application interface / `Anela.Heblo.Adapters.FileSystem` implementation) rather than introducing a new, narrowly-scoped interface dedicated to the reprint flow. This was chosen as the more surgical option since the abstraction and its DI wiring already exist and are already unconditionally registered. Please confirm this is preferred over introducing a separate interface (e.g. `ITempFileStager`) scoped only to `ExpeditionListArchive`.

**Answer:** Confirmed — extend `ITemporaryFileAccessor`. Do not introduce a new `ExpeditionListArchive`-scoped interface.

**Rationale:** `ITemporaryFileAccessor` already lives under `Features/ExpeditionList/Contracts` (not archive-specific), is already injected into the sibling `ExpeditionListService`, and is already registered unconditionally in DI — a second interface with an identical purpose would be pure duplication with no testability or SRP benefit, violating CLAUDE.md's "surgical changes" and reuse-over-reinvention principles.

### Question 2
**Deviation from suggested fix**: Please confirm the decision to leave `IPrintQueueSink.SendAsync`'s signature unchanged (see Background) rather than following the brief's literal suggestion of a `Stream`-based `IPrintQueueSink`. If the architecture review intends for `IPrintQueueSink` itself to eventually move to a stream-based contract across all consumers, that should be scoped as its own, larger piece of work covering `ExpeditionListService`, `CombinedPrintQueueSink`, and all four sink implementations — not bundled into this fix.

**Answer:** Confirmed — leave `IPrintQueueSink.SendAsync` unchanged. Proceed with the `ITemporaryFileAccessor` approach exactly as scoped in this spec; do not touch `IPrintQueueSink` or any of its implementations.

**Rationale:** The brief's own "why it matters" section only cites untestability and misplaced responsibility in `ReprintExpeditionListHandler` — both are fully resolved by extending `ITemporaryFileAccessor`, so the `Stream`-based `IPrintQueueSink` was a suggested mechanism, not the goal; the investigation in the Background section shows that signature change would ripple into batch printing, dual-sink fan-out, and blob-naming logic that the finding never touched, which is exactly the over-reach CLAUDE.md's "surgical changes" rule warns against. If a stream-based `IPrintQueueSink` is later desired, it should be filed as its own arch-review finding with its own blast-radius review.

### Question 3
**Existing test disposition**: Should the current `ReprintExpeditionListHandlerTests.cs` filesystem-leak-detection tests (which assert against `Path.GetTempPath()` / `Directory.EnumerateFiles`) be deleted outright once the handler no longer touches the filesystem, or should an equivalent real-I/O integration test be kept at the adapter level for `FileSystemTemporaryFileAccessor` instead? FR-5 assumes the latter (delete from the handler test, add at the adapter level) — please confirm.

**Answer:** Confirmed — delete the filesystem-leak-detection assertions from `ReprintExpeditionListHandlerTests.cs` and add equivalent real-I/O coverage as new adapter-level tests for `FileSystemTemporaryFileAccessor.CreateFromStreamAsync`.

**Rationale:** Once the handler depends only on the `ITemporaryFileAccessor` mock, it has no filesystem behavior left to assert against, so retaining `Path.GetTempPath()`/`Directory.EnumerateFiles` checks there would be dead weight testing nothing meaningful; the real I/O behavior (file created, contents match, no orphan on failure) is a concern of the adapter implementation and belongs in an adapter-layer test, consistent with the "I/O placement rule" in `docs/architecture/filesystem.md` that keeps concrete I/O — and its tests — in the adapter project.
