# Pattern: Creating a New Backend Feature

Follow this order every time a new feature is added:

1. **Domain** — define entity and repository interface in `Domain/Features/{Feature}/`
2. **Application** — create MediatR handler in `Application/Features/{Feature}/UseCases/{Action}{Entity}Handler.cs`
   - Request and Response must be **classes** (not records) — see `decisions/dto-classes-not-records.md`
3. **Persistence** — create EF Core entity configuration in `Persistence/{Feature}/`
4. **API** — create controller in `API/Controllers/{Feature}Controller.cs`
   - Route: `/api/{controller}` (REST convention)
   - Controller calls `IMediator.Send(request)`
5. **Registration** — update `{Feature}Module.cs` and `PersistenceModule.cs`

## Naming
- Controller: `{Feature}Controller`
- Handler: `{Action}{Entity}Handler`
- Request/Response: `{Action}{Entity}Request` / `{Action}{Entity}Response`
- DTO: `{Entity}Dto`
