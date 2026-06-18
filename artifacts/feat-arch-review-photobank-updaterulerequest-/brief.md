## Module
Photobank

## Finding
`UpdateRuleRequest` (a MediatR request type) is used directly as the `[FromBody]` parameter in `PhotobankController.UpdateRule`:

```csharp
// PhotobankController.cs:299-314
public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
    int id,
    [FromBody] UpdateRuleRequest request,   // body type IS the MediatR request
    CancellationToken cancellationToken = default)
{
    var command = new UpdateRuleRequest
    {
        Id = id,              // route value wins
        PathPattern = request.PathPattern,
        TagName = request.TagName,
        IsActive = request.IsActive,
        SortOrder = request.SortOrder,
    };
    ...
```

`UpdateRuleRequest` declares `public int Id { get; set; }` (line 7 in `UpdateRuleRequest.cs`). Because the type is used as the body, the OpenAPI schema for `PUT /api/photobank/settings/rules/{id}` shows an `id` field in the request body. Clients (and the auto-generated TypeScript client) will include `id` in their JSON payload, but the controller overwrites it with the route parameter — the body value is silently discarded.

## Why it matters
- The auto-generated TypeScript API client will serialize and send `id` in the body, creating a misleading contract.
- Any future maintainer reading the schema or client type sees an `id` field they can set, but it has no effect — a debugging trap.
- Violates the module's own pattern: every other write endpoint uses a dedicated `contracts/*Body.cs` type that contains only the fields the client is actually allowed to control.

## Suggested fix
Introduce `UpdateRuleBody` in `contracts/`:
```csharp
// contracts/UpdateRuleBody.cs
public class UpdateRuleBody
{
    public string PathPattern { get; set; } = null!;
    public string TagName { get; set; } = null!;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}
```
Change the controller to:
```csharp
public async Task<ActionResult<UpdateRuleResponse>> UpdateRule(
    int id,
    [FromBody] UpdateRuleBody body,
    CancellationToken ct)
{
    var command = new UpdateRuleRequest { Id = id, PathPattern = body.PathPattern, ... };
```
This removes `Id` from the OpenAPI body schema and eliminates the silent override.

---
_Filed by daily arch-review routine on 2026-06-14._