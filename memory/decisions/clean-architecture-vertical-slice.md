# Decision: Clean Architecture with Vertical Slice Organization

**Decision:** Use Clean Architecture but organize by feature (vertical slice), not by layer.

**Why:** Avoids the "horizontal layer" problem where a single feature's code is scattered across layers. Each feature owns its full stack — domain, application, persistence, API — making features self-contained and easier to reason about.

**How to apply:**
- New features go in `Domain/Features/{Feature}/`, `Application/Features/{Feature}/UseCases/`, `Persistence/{Feature}/`, `API/Controllers/`
- Each feature has its own `{Feature}Module.cs` for DI registration
- MediatR pattern: Controller → IMediator.Send(Request) → Handler → Response
- See `docs/architecture/filesystem.md` for directory placement rules
