### task: use-imapper-in-classification-history-handler

**Goal**
Replace the hand-written 17-line `Select` projection in `GetClassificationHistoryHandler.Handle` with a single `IMapper.Map<List<ClassificationHistoryDto>>(...)` call, injecting `IMapper` via the constructor. This makes the handler match its sibling `GetClassificationRulesHandler` and eliminates a duplicate of the mapping already fully defined in `InvoiceClassificationMappingProfile`. Output must remain byte-for-byte equivalent.

**File to touch (only this one)**
`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs`

Do NOT touch: `InvoiceClassificationMappingProfile`, `ClassificationHistoryDto`, the domain entity, the repository, the request/response contracts, the controller, or DI registration. `IMapper` is already registered in DI, so no registration change is needed.

**Exact changes**

1. Add `using AutoMapper;` to the using block (top of file, alongside the existing usings — place it first to match the sibling handler's ordering).

2. Add a mapper field next to the existing fields:
   - Keep `private readonly IClassificationHistoryRepository _historyRepository;` and `private readonly ILogger<GetClassificationHistoryHandler> _logger;`
   - Add `private readonly IMapper _mapper;`

3. Change the constructor signature to inject `IMapper` and assign it. The constructor becomes:
   ```csharp
   public GetClassificationHistoryHandler(
       IClassificationHistoryRepository historyRepository,
       IMapper mapper,
       ILogger<GetClassificationHistoryHandler> logger)
   {
       _historyRepository = historyRepository;
       _mapper = mapper;
       _logger = logger;
   }
   ```
   (Parameter order exactly: `historyRepository`, `mapper`, `logger` — as specified in FR-1.)

4. In `Handle`, replace the entire manual projection block (current lines 31-47, the `var historyDtos = historyItems.Select(history => new ClassificationHistoryDto { ... }).ToList();`) with a single line:
   ```csharp
   var historyDtos = _mapper.Map<List<ClassificationHistoryDto>>(historyItems);
   ```
   No per-property assignment may remain. Leave the `_historyRepository.GetPagedHistoryAsync(...)` call above it and the `return new GetClassificationHistoryResponse { Items = historyDtos, TotalCount = totalCount, Page = request.Page, PageSize = request.PageSize };` block below it unchanged.

**Important correctness notes (why this is safe)**
- `InvoiceClassificationMappingProfile` already maps all 14 `ClassificationHistoryDto` properties, including the two non-convention ones: `InvoiceId` ← `AbraInvoiceId`, and `RuleName` ← `ClassificationRule?.Name` (null-safe when the rule is null). The other 12 properties are same-name convention mappings. Do not re-add any of these manually and do not modify the profile.
- The `_logger` field is retained even if currently unused elsewhere — do not remove it; scope is limited to the mapping refactor.

**Optional (recommended) unit test**
If adding a test, create it under the module's test project mirroring the existing test layout for InvoiceClassification handlers (locate the sibling handler/profile tests first; do not invent a new structure). The test should map a `ClassificationHistory` domain instance to `ClassificationHistoryDto` via the real `InvoiceClassificationMappingProfile` (construct a `MapperConfiguration` with that profile, `AssertConfigurationIsValid()`, then `CreateMapper()`), asserting:
- `InvoiceId` equals the source `AbraInvoiceId`.
- `RuleName` equals `ClassificationRule.Name` when the rule is present.
- `RuleName` is null when `ClassificationRule` is null (second case).
- The remaining 12 properties are copied identically.
This guards against silent output drift on the convention-mapped properties. If the surrounding test infrastructure does not already exist and creating it would exceed the surgical scope, skip the test rather than scaffolding a new test project.

**Acceptance criteria**
- The handler declares `private readonly IMapper _mapper;`, the constructor signature is exactly `(IClassificationHistoryRepository historyRepository, IMapper mapper, ILogger<GetClassificationHistoryHandler> logger)`, and `Handle` contains the single `_mapper.Map<List<ClassificationHistoryDto>>(historyItems)` call with zero manual property assignments.
- `using AutoMapper;` is present; no unused usings introduced.
- The response envelope (`Items`, `TotalCount`, `Page`, `PageSize`) is unchanged and populated as before.
- `dotnet build` succeeds.
- `dotnet format` reports no changes (i.e., run it and confirm a clean diff on the touched file).
- If a unit test was added, it passes.
