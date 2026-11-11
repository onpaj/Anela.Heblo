# Impl switch-consumer r1
- GiftPackageManufactureService now depends on `IGiftPackageBomSource` (same ctor position); Manufacture domain using removed.
- Updated service tests, atomicity integration tests and module DI test to the new contract/model.
- `dotnet test --filter GiftPackage`: 52 pass; 5 Postgres-Testcontainers integration tests cannot run here (no Docker) but compile.
