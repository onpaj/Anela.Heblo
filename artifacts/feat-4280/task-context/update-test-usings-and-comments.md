### task: update-test-usings-and-comments

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs:1-7`
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs:1-14`
- Modify: `backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs:1-7`
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:908`, `:977`

- [ ] **Step 1: `GetGroupMembersHandlerTests.cs` — add the new using, keep `Contracts` (needed for `UserDto`)**

Current top of file:
```csharp
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;
```
Change to:
```csharp
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Shared;
```

- [ ] **Step 2: `GraphServiceTests.cs` — add the new using, keep `Contracts`**

Current top of file:
```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```
Change to:
```csharp
using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
```

- [ ] **Step 3: `EntraAccessUserSourceAdapterTests.cs` — add the new using, keep `Contracts`**

Current top of file:
```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;
```
Change to:
```csharp
using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 4: `ModuleBoundariesTests.cs` — fix the 2 stale comment/message occurrences (text only, no logic change)**

At line 908 (inside the `SdkExceptionAllowlist` header comment), change:
```csharp
    // GraphServiceException) defined in UserManagement.Contracts instead.
```
to:
```csharp
    // GraphServiceException) defined in UserManagement.Infrastructure.Exceptions instead.
```

At line 977 (inside the `violations.Should().BeEmpty(...)` message string), change:
```csharp
            "UserManagement.Contracts) instead, or add an allowlist entry with justification " +
```
to:
```csharp
            "UserManagement.Infrastructure.Exceptions) instead, or add an allowlist entry with justification " +
```
Do not touch any other line in this file — in particular, do not touch the `SdkExceptionAllowlist` entries themselves (`MeetingTasks.Services.GraphPlannerService`, `CatalogDocuments.Services.GraphCatalogDocumentsStorage`) or the reflection/enumeration logic; they are unrelated to this change.

- [ ] **Step 5: Build and run the full test suite**

Run: `cd backend && dotnet build`
Expected: 0 errors across all projects (production + test).

Run: `dotnet test --filter "FullyQualifiedName~UserManagement|FullyQualifiedName~ModuleBoundariesTests"`
Expected: all `UserManagement`-namespace tests pass (including `GetGroupMembersHandlerTests`, `GraphServiceTests`, `EntraAccessUserSourceAdapterTests`), and `ModuleBoundariesTests.Application_types_should_not_catch_SDK_exception_types_directly` still passes (its enforcement logic is unaffected — see design.r1.md).

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/EntraAccessUserSourceAdapterTests.cs \
        backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(usermanagement): fix usings and stale comments for relocated Graph exceptions"
```

---
