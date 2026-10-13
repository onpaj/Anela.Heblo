### task: update-flexi-department-client

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs`

**Context:** This file implements the Domain `IDepartmentClient` interface and returns `Department` instances. It already disambiguates the Domain interface from the FlexiBee SDK's own `IDepartmentClient` (aliased locally) by fully qualifying the base interface in the class declaration. Only the `using` for the plain `Department` type and the fully-qualified base-interface reference need to change.

- [ ] **Step 1: View current file content**

Run:
```bash
cat backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs
```
Expected top of file:
```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient
```

- [ ] **Step 2: Update the `using` and the base-interface qualification**

Change:
```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient
```
to:
```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Microsoft.Extensions.Caching.Memory;
using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;

namespace Anela.Heblo.Adapters.Flexi.Accounting.Departments;

public class FlexiDepartmentClient : Domain.Features.Analytics.IDepartmentClient
```

Every other line in the file — the constructor, `GetDepartmentsAsync`, `GetDepartmentByIdAsync`, caching logic — is unchanged. Do not touch the `IDepartmentClient` alias line; it must keep pointing at the FlexiBee SDK type so `_client` (the injected constructor parameter) still resolves to the SDK client, not the Domain interface.

- [ ] **Step 3: Verify no other reference to the old namespace remains in this file**

Run:
```bash
grep -n "InvoiceClassification" backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs
```
Expected: no output (empty).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs
git commit -m "refactor(flexi): point FlexiDepartmentClient at Domain.Features.Analytics"
```

---
