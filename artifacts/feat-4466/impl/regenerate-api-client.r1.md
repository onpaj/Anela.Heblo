# Implementation: regenerate-api-client (r1)

- Ran `dotnet tool restore`, `dotnet build backend/src/Anela.Heblo.API`, then `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`.
- `frontend/src/api/generated/api-client.ts` (tracked) now has `availableTransitions?: ManufactureOrderState[]` on `GetManufactureOrderResponse` (+ its interface, init and toJSON). Diff: 12 insertions, no other changes.
