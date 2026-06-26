## Module
BackgroundJobs

## Finding
Two request-body DTO classes are defined directly inside the API layer controller file, violating the project rule that the API project must never own DTOs:

- `UpdateJobStatusRequestBody` — `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` lines 128–134
- `UpdateJobCronRequestBody` — `backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs` lines 136–146

Both classes live in the `Anela.Heblo.API.Controllers` namespace and are defined at the bottom of the controller file.

## Why it matters
`development_guidelines.md` states explicitly: *"API project never defines or owns DTOs"* and *"DTOs defined in API or Xcc — Breaks ownership, violates boundaries."* Having DTOs in the API project couples HTTP contracts to the host layer, prevents other consumers from referencing them without taking on the API project dependency, and breaks the ownership model where each module's contracts are self-contained.

## Suggested fix
Move both classes into the existing `Contracts/` folder of the BackgroundJobs application module:

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobStatusRequestBody.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/UpdateJobCronRequestBody.cs`

Update the `using` in the controller accordingly. No behavioural change required.

---
_Filed by daily arch-review routine on 2026-05-27._