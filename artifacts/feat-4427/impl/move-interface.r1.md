# move-interface r1
- git mv ITransportBoxCompletionService.cs from Domain/Features/Logistics/Transport to Application/Features/Logistics/Services; namespace changed to Anela.Heblo.Application.Features.Logistics.Services; contents otherwise unchanged.
- TransportBoxCompletionService.cs and LogisticsModule.cs already import the Services namespace / same namespace; no using changes needed (Domain usings still required).
- `dotnet build Anela.Heblo.sln`: succeeded. dotnet format verify on moved file: clean.
- Logistics tests: 305 pass, 7 fail; all 7 are Testcontainers/PostgreSQL integration tests that cannot start (no Docker in this environment), unrelated to the change.
