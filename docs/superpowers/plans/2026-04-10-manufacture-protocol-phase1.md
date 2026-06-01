# Manufacture Protocol Phase 1 — SDK + Domain + Contracts

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the data-contract foundation for the Manufacture Protocol PDF: expose the Flexi document `code` in the SDK response DTO, add 5 new auto-captured `FlexiDoc*` fields to `ManufactureOrder`, and introduce `SubmitManufactureClientResponse` as the new return type for `IManufactureClient.SubmitManufactureAsync`.

**Architecture:** Three additive changes with no behaviour change — a one-property DTO extension in the sibling FlexiBeeSDK repo, 10 new nullable columns on the domain entity, and a new value-object class that replaces `Task<string>` with `Task<SubmitManufactureClientResponse>` on the interface. Downstream compile errors from the interface change are **expected** and are fixed in Phase 2.

**Tech Stack:** .NET 8, C#, Newtonsoft.Json (FlexiBeeSDK), xUnit, FluentAssertions.

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `/Users/pajgrtondrej/Work/GitHub/FlexiBeeSDK/src/Rem.FlexiBeeSDK.Model/Response/Result.cs` | Add `Code` property mapped to `"code"` JSON key |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` | 5 new `FlexiDoc*` string + DateTime? field pairs |
| Create | `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs` | New rich return shape for `SubmitManufactureAsync` |
| Modify | `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs` | Change return type from `Task<string>` to `Task<SubmitManufactureClientResponse>` |

---

## Task 1: Extend FlexiBeeSDK Result DTO with `Code`

**Files:**
- Modify: `/Users/pajgrtondrej/Work/GitHub/FlexiBeeSDK/src/Rem.FlexiBeeSDK.Model/Response/Result.cs`

- [ ] **Step 1: Add `Code` property to `Result.cs`**

Replace the entire file content with:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Rem.FlexiBeeSDK.Model.Response;

public class Result
{
    [JsonProperty("id")]
    public string? Id { get; set; }
    [JsonProperty("ref")]
    public string? Reference { get; set; }
    [JsonProperty("code")]
    public string? Code { get; set; }
    [JsonProperty("request-id")]
    public string? Requestid { get; set; }
    [JsonProperty("errors")]
    public List<Error>? Errors { get; set; }
}
```

Note: Flexi REST `results[]` entries use `"code"` (not `"kod"` which appears on the entity itself). Both represent the user-visible document code.

- [ ] **Step 2: Build the SDK**

```bash
cd /Users/pajgrtondrej/Work/GitHub/FlexiBeeSDK
dotnet build
```

Expected: clean build, no warnings about `Code`.

- [ ] **Step 3: Commit in the SDK repo**

```bash
cd /Users/pajgrtondrej/Work/GitHub/FlexiBeeSDK
git add src/Rem.FlexiBeeSDK.Model/Response/Result.cs
git commit -m "feat(response): expose document code on Result DTO"
```

---

## Task 2: Add 5 new `FlexiDoc*` fields to `ManufactureOrder`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`

- [ ] **Step 1: Append the 10 new properties after `ErpDiscardResidueDocumentNumberDate`**

Open `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`.

After line 32 (`public DateTime? ErpDiscardResidueDocumentNumberDate { get; set; }`), insert:

```csharp
    // ABRA Flexi stock-document codes captured from SubmitManufactureAsync results.
    // 1. Material issue for semi-product (V-VYDEJ-MATERIAL, phase A)
    public string? FlexiDocMaterialIssueForSemiProduct { get; set; }
    public DateTime? FlexiDocMaterialIssueForSemiProductDate { get; set; }

    // 2. Semi-product receipt (V-PRIJEM-POLOTOVAR, phase A)
    public string? FlexiDocSemiProductReceipt { get; set; }
    public DateTime? FlexiDocSemiProductReceiptDate { get; set; }

    // 3. Semi-product issue for product (V-VYDEJ-POLOTOVAR, phase B)
    public string? FlexiDocSemiProductIssueForProduct { get; set; }
    public DateTime? FlexiDocSemiProductIssueForProductDate { get; set; }

    // 4. Material issue for product (V-VYDEJ-MATERIAL, phase B, optional)
    public string? FlexiDocMaterialIssueForProduct { get; set; }
    public DateTime? FlexiDocMaterialIssueForProductDate { get; set; }

    // 5. Product receipt (V-PRIJEM-VYROBEK, phase B)
    public string? FlexiDocProductReceipt { get; set; }
    public DateTime? FlexiDocProductReceiptDate { get; set; }
```

The existing `WeightWithinTolerance` and `WeightDifference` fields remain after these new properties.

- [ ] **Step 2: Build the solution**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/Anela.Heblo.sln
```

Expected: clean build. EF Core will not complain at build time — the missing columns are handled in Phase 4's migration.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs
git commit -m "feat(manufacture): add 5 Flexi stock-document fields to ManufactureOrder"
```

---

## Task 3: Introduce `SubmitManufactureClientResponse` and update `IManufactureClient`

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs`

- [ ] **Step 1: Create `SubmitManufactureClientResponse.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture;

public class SubmitManufactureClientResponse
{
    public string ManufactureId { get; set; } = null!;
    public string? MaterialIssueForSemiProductDocCode { get; set; }
    public string? SemiProductReceiptDocCode { get; set; }
    public string? SemiProductIssueForProductDocCode { get; set; }
    public string? MaterialIssueForProductDocCode { get; set; }
    public string? ProductReceiptDocCode { get; set; }
}
```

- [ ] **Step 2: Update `IManufactureClient.SubmitManufactureAsync` return type**

In `backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs`, change line 5 from:

```csharp
    Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default);
```

to:

```csharp
    Task<SubmitManufactureClientResponse> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Build to confirm expected compile errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/Anela.Heblo.sln 2>&1 | grep -E "error|Error"
```

Expected: compile errors in `FlexiManufactureClient.cs` (returns `Task<string>`) and `SubmitManufactureHandler.cs` (assigns result to `string`). These are **intentional** — Phase 2 fixes all call sites. Do **not** fix them here.

- [ ] **Step 4: Commit the contract files only (not the broken call sites)**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs \
        backend/src/Anela.Heblo.Domain/Features/Manufacture/IManufactureClient.cs
git commit -m "feat(manufacture): introduce SubmitManufactureClientResponse"
```

---

## Final verification

- [ ] **Confirm 3 commits are on the branch**

```bash
git log --oneline -5
```

Expected: three commits with messages matching the above (plus any existing commits from `main`).

- [ ] **Confirm SDK repo has its commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/FlexiBeeSDK
git log --oneline -3
```

Expected: top commit is `feat(response): expose document code on Result DTO`.

- [ ] **Confirm new domain files exist**

```bash
ls backend/src/Anela.Heblo.Domain/Features/Manufacture/SubmitManufactureClientResponse.cs
```
