# Task Plan: feat-3280

## Tasks

### task: write-validator-tests

**Goal**: Write unit tests for `CreatePurchaseOrderRequestValidator` and `CreatePurchaseOrderLineRequestValidator`.

**File to create**:
`backend/test/Anela.Heblo.Tests/Features/Purchase/CreatePurchaseOrderRequestValidatorTests.cs`

**Steps**:
1. Create test file following `GetCatalogDetailRequestValidatorTests` pattern
2. Cover all validation rules for both validators (see spec.r1.md)
3. Run `dotnet test --filter FullyQualifiedName~CreatePurchaseOrderRequestValidatorTests`
4. Fix any failing tests

**Success criteria**: All tests in the new file pass with `dotnet test`.
