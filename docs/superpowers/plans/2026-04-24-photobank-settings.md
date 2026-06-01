# Photobank Admin Settings Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the admin-only Photobank settings page at `/marketing/photobank/settings` with Index Roots and Tag Rules tabs, backed by existing API endpoints.

**Architecture:** Fix two backend DTOs that are missing fields already in the domain entity, then build a new React settings page with two tab components and a dedicated React Query hooks file. All backend CRUD endpoints already exist — this is purely frontend + minor DTO work.

**Tech Stack:** .NET 8 / C# (backend DTOs + handlers), React 18 + TypeScript, TanStack React Query v5, Tailwind CSS, lucide-react, MSAL (`@azure/msal-react`), react-router-dom, xUnit + FluentAssertions + Moq (backend tests), Jest + React Testing Library (frontend tests)

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/IndexRootDto.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRootRequest.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetRoots/GetRootsHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddRoot/AddRootHandler.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Features/Photobank/GetRootsHandlerTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Features/Photobank/AddRootHandlerTests.cs` |
| Create | `frontend/src/api/hooks/usePhotobankSettings.ts` |
| Create | `frontend/src/api/hooks/__tests__/usePhotobankSettings.test.ts` |
| Create | `frontend/src/components/marketing/photobank/settings/IndexRootsTab.tsx` |
| Create | `frontend/src/components/marketing/photobank/settings/TagRulesTab.tsx` |
| Create | `frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx` |
| Create | `frontend/src/components/marketing/photobank/__tests__/IndexRootsTab.test.tsx` |
| Create | `frontend/src/components/marketing/photobank/__tests__/TagRulesTab.test.tsx` |
| Modify | `frontend/src/App.tsx` (add import + route) |
| Modify | `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx` (add settings link) |

---

## Task 1: Fix backend DTOs

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/IndexRootDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRootRequest.cs`

- [ ] **Step 1: Replace IndexRootDto.cs**

Full file content — adds `DriveId`, `RootItemId`, `LastIndexedAt`:

```csharp
using System;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class IndexRootDto
    {
        public int Id { get; set; }
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string? DriveId { get; set; }
        public string? RootItemId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastIndexedAt { get; set; }
    }
}
```

- [ ] **Step 2: Replace AddRootRequest.cs**

Full file content — adds required `DriveId` and `RootItemId`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRootRequest : IRequest<AddRootResponse>
    {
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string DriveId { get; set; } = null!;
        public string RootItemId { get; set; } = null!;
    }

    public class AddRootResponse : BaseResponse
    {
        public int Id { get; set; }
    }
}
```

---

## Task 2: Fix backend handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetRoots/GetRootsHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddRoot/AddRootHandler.cs`

- [ ] **Step 1: Update GetRootsHandler.cs to map new fields**

Full file content:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetRoots
{
    public class GetRootsHandler : IRequestHandler<GetRootsRequest, GetRootsResponse>
    {
        private readonly IPhotobankRepository _repository;

        public GetRootsHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetRootsResponse> Handle(GetRootsRequest request, CancellationToken cancellationToken)
        {
            var roots = await _repository.GetRootsAsync(cancellationToken);

            return new GetRootsResponse
            {
                Roots = roots.Select(r => new IndexRootDto
                {
                    Id = r.Id,
                    SharePointPath = r.SharePointPath,
                    DisplayName = r.DisplayName,
                    DriveId = r.DriveId,
                    RootItemId = r.RootItemId,
                    IsActive = r.IsActive,
                    CreatedAt = r.CreatedAt,
                    LastIndexedAt = r.LastIndexedAt,
                }).ToList(),
            };
        }
    }
}
```

- [ ] **Step 2: Update AddRootHandler.cs to persist new fields**

Full file content:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot
{
    public class AddRootHandler : IRequestHandler<AddRootRequest, AddRootResponse>
    {
        private readonly IPhotobankRepository _repository;

        public AddRootHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<AddRootResponse> Handle(AddRootRequest request, CancellationToken cancellationToken)
        {
            var root = new PhotobankIndexRoot
            {
                SharePointPath = request.SharePointPath.Trim(),
                DisplayName = request.DisplayName?.Trim(),
                DriveId = request.DriveId.Trim(),
                RootItemId = request.RootItemId.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            var created = await _repository.AddRootAsync(root, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new AddRootResponse { Id = created.Id };
        }
    }
}
```

- [ ] **Step 3: Verify the backend still builds**

```bash
cd /path/to/worktree
dotnet build backend/
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 3: Backend handler tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Photobank/GetRootsHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Photobank/AddRootHandlerTests.cs`

- [ ] **Step 1: Create GetRootsHandlerTests.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetRoots;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class GetRootsHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repoMock = new();

    private GetRootsHandler CreateHandler() => new(_repoMock.Object);

    [Fact]
    public async Task Handle_ReturnsMappedRoots_WithAllFields()
    {
        // Arrange
        var lastIndexed = new DateTime(2026, 4, 24, 3, 0, 0, DateTimeKind.Utc);
        var roots = new List<PhotobankIndexRoot>
        {
            new()
            {
                Id = 1,
                SharePointPath = "/Fotky/Produkty",
                DisplayName = "Produkty",
                DriveId = "drive-abc",
                RootItemId = "item-xyz",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastIndexedAt = lastIndexed,
            }
        };
        _repoMock
            .Setup(r => r.GetRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roots);

        // Act
        var response = await CreateHandler().Handle(new GetRootsRequest(), CancellationToken.None);

        // Assert
        response.Roots.Should().HaveCount(1);
        var dto = response.Roots[0];
        dto.Id.Should().Be(1);
        dto.SharePointPath.Should().Be("/Fotky/Produkty");
        dto.DisplayName.Should().Be("Produkty");
        dto.DriveId.Should().Be("drive-abc");
        dto.RootItemId.Should().Be("item-xyz");
        dto.IsActive.Should().BeTrue();
        dto.LastIndexedAt.Should().Be(lastIndexed);
    }

    [Fact]
    public async Task Handle_MapsNullFields_WhenEntityHasNoOptionalValues()
    {
        // Arrange
        var roots = new List<PhotobankIndexRoot>
        {
            new()
            {
                Id = 2,
                SharePointPath = "/Fotky",
                DisplayName = null,
                DriveId = null,
                RootItemId = null,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                LastIndexedAt = null,
            }
        };
        _repoMock
            .Setup(r => r.GetRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roots);

        // Act
        var response = await CreateHandler().Handle(new GetRootsRequest(), CancellationToken.None);

        // Assert
        var dto = response.Roots[0];
        dto.DisplayName.Should().BeNull();
        dto.DriveId.Should().BeNull();
        dto.RootItemId.Should().BeNull();
        dto.LastIndexedAt.Should().BeNull();
        dto.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoRootsExist()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PhotobankIndexRoot>());

        // Act
        var response = await CreateHandler().Handle(new GetRootsRequest(), CancellationToken.None);

        // Assert
        response.Roots.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Create AddRootHandlerTests.cs**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class AddRootHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repoMock = new();

    private AddRootHandler CreateHandler() => new(_repoMock.Object);

    [Fact]
    public async Task Handle_PersistsDriveIdAndRootItemId()
    {
        // Arrange
        PhotobankIndexRoot? savedRoot = null;
        _repoMock
            .Setup(r => r.AddRootAsync(It.IsAny<PhotobankIndexRoot>(), It.IsAny<CancellationToken>()))
            .Callback<PhotobankIndexRoot, CancellationToken>((root, _) => savedRoot = root)
            .ReturnsAsync((PhotobankIndexRoot root, CancellationToken _) =>
            {
                root.Id = 42;
                return root;
            });

        var request = new AddRootRequest
        {
            SharePointPath = "/Fotky/Produkty",
            DisplayName = "Produkty",
            DriveId = "drive-abc",
            RootItemId = "item-xyz",
        };

        // Act
        var response = await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.Id.Should().Be(42);
        savedRoot.Should().NotBeNull();
        savedRoot!.DriveId.Should().Be("drive-abc");
        savedRoot.RootItemId.Should().Be("item-xyz");
        savedRoot.SharePointPath.Should().Be("/Fotky/Produkty");
        savedRoot.DisplayName.Should().Be("Produkty");
        savedRoot.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TrimsWhitespaceFromStringFields()
    {
        // Arrange
        PhotobankIndexRoot? savedRoot = null;
        _repoMock
            .Setup(r => r.AddRootAsync(It.IsAny<PhotobankIndexRoot>(), It.IsAny<CancellationToken>()))
            .Callback<PhotobankIndexRoot, CancellationToken>((root, _) => savedRoot = root)
            .ReturnsAsync((PhotobankIndexRoot root, CancellationToken _) => root);

        var request = new AddRootRequest
        {
            SharePointPath = "  /Fotky  ",
            DisplayName = "  Název  ",
            DriveId = "  drive-abc  ",
            RootItemId = "  item-xyz  ",
        };

        // Act
        await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        savedRoot!.SharePointPath.Should().Be("/Fotky");
        savedRoot.DisplayName.Should().Be("Název");
        savedRoot.DriveId.Should().Be("drive-abc");
        savedRoot.RootItemId.Should().Be("item-xyz");
    }

    [Fact]
    public async Task Handle_CallsSaveChanges()
    {
        // Arrange
        _repoMock
            .Setup(r => r.AddRootAsync(It.IsAny<PhotobankIndexRoot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PhotobankIndexRoot root, CancellationToken _) => root);

        var request = new AddRootRequest
        {
            SharePointPath = "/Fotky",
            DriveId = "drive-1",
            RootItemId = "item-1",
        };

        // Act
        await CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run backend tests**

```bash
dotnet test backend/ --filter "FullyQualifiedName~GetRootsHandlerTests|FullyQualifiedName~AddRootHandlerTests"
```

Expected: all 6 tests pass, 0 failures.

- [ ] **Step 4: Commit**

```bash
cd /path/to/worktree
git add backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/IndexRootDto.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/Contracts/AddRootRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetRoots/GetRootsHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddRoot/AddRootHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/GetRootsHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Photobank/AddRootHandlerTests.cs
git commit -m "fix(photobank): expose DriveId, RootItemId, LastIndexedAt in IndexRootDto and AddRootRequest"
```

---

## Task 4: Frontend hooks — usePhotobankSettings.ts

**Files:**
- Create: `frontend/src/api/hooks/usePhotobankSettings.ts`

- [ ] **Step 1: Create usePhotobankSettings.ts**

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

// ---- Types ----------------------------------------------------------------

export interface IndexRootDto {
  id: number;
  sharePointPath: string;
  displayName: string | null;
  driveId: string | null;
  rootItemId: string | null;
  isActive: boolean;
  createdAt: string;
  lastIndexedAt: string | null;
}

export interface TagRuleDto {
  id: number;
  pathPattern: string;
  tagName: string;
  isActive: boolean;
  sortOrder: number;
}

export interface ReapplyRulesResult {
  photosUpdated: number;
}

export interface AddIndexRootInput {
  sharePointPath: string;
  displayName: string | null;
  driveId: string;
  rootItemId: string;
}

export interface AddTagRuleInput {
  pathPattern: string;
  tagName: string;
  sortOrder: number;
}

// ---- Helpers ----------------------------------------------------------------

function getClientAndBaseUrl(): {
  apiClient: ReturnType<typeof getAuthenticatedApiClient>;
  baseUrl: string;
} {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return { apiClient, baseUrl };
}

async function apiFetch(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, { method: "GET" });
  if (!response.ok) {
    throw new Error(`Photobank settings API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

async function apiPost(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
  body: unknown,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Photobank settings API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

async function apiDelete(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
): Promise<Response> {
  const response = await (apiClient as any).http.fetch(url, { method: "DELETE" });
  if (!response.ok) {
    throw new Error(`Photobank settings API error: ${response.status} ${response.statusText}`);
  }
  return response;
}

const ROOTS_QUERY_KEY = [...QUERY_KEYS.photobank, "settings", "roots"] as const;
const RULES_QUERY_KEY = [...QUERY_KEYS.photobank, "settings", "rules"] as const;

// ---- Index Roots Hooks -------------------------------------------------------

export const useIndexRoots = () => {
  return useQuery<IndexRootDto[]>({
    queryKey: ROOTS_QUERY_KEY,
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/photobank/settings/roots`);
      const data = await response.json();
      return data.roots ?? [];
    },
  });
};

export const useAddIndexRoot = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddIndexRootInput) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/photobank/settings/roots`,
        input,
      );
      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROOTS_QUERY_KEY });
    },
  });
};

export const useDeleteIndexRoot = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      await apiDelete(apiClient, `${baseUrl}/api/photobank/settings/roots/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROOTS_QUERY_KEY });
    },
  });
};

// ---- Tag Rules Hooks ---------------------------------------------------------

export const useTagRules = () => {
  return useQuery<TagRuleDto[]>({
    queryKey: RULES_QUERY_KEY,
    queryFn: async () => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiFetch(apiClient, `${baseUrl}/api/photobank/settings/rules`);
      const data = await response.json();
      return data.rules ?? [];
    },
  });
};

export const useAddTagRule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: AddTagRuleInput) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/photobank/settings/rules`,
        input,
      );
      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RULES_QUERY_KEY });
    },
  });
};

export const useDeleteTagRule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      await apiDelete(apiClient, `${baseUrl}/api/photobank/settings/rules/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RULES_QUERY_KEY });
    },
  });
};

export const useReapplyTagRules = () => {
  return useMutation({
    mutationFn: async (): Promise<ReapplyRulesResult> => {
      const { apiClient, baseUrl } = getClientAndBaseUrl();
      const response = await apiPost(
        apiClient,
        `${baseUrl}/api/photobank/settings/rules/reapply`,
        {},
      );
      return response.json();
    },
  });
};
```

---

## Task 5: Frontend hook tests — usePhotobankSettings.test.ts

**Files:**
- Create: `frontend/src/api/hooks/__tests__/usePhotobankSettings.test.ts`

- [ ] **Step 1: Create usePhotobankSettings.test.ts**

```typescript
import { renderHook, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import {
  useIndexRoots,
  useTagRules,
  useAddIndexRoot,
  useDeleteIndexRoot,
  useAddTagRule,
  useDeleteTagRule,
  useReapplyTagRules,
} from "../usePhotobankSettings";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

// ---- Mock data ----------------------------------------------------------------

const mockRoot = {
  id: 1,
  sharePointPath: "/Fotky/Produkty",
  displayName: "Produkty",
  driveId: "drive-abc",
  rootItemId: "item-xyz",
  isActive: true,
  createdAt: "2026-01-01T00:00:00Z",
  lastIndexedAt: "2026-04-24T03:00:00Z",
};

const mockRule = {
  id: 1,
  pathPattern: "/Fotky/Produkty/*",
  tagName: "produkty",
  isActive: true,
  sortOrder: 10,
};

// ---- Helpers ------------------------------------------------------------------

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
}

function createMockClient(fetchImpl: jest.Mock) {
  return {
    baseUrl: "http://localhost:5001",
    http: { fetch: fetchImpl },
  };
}

// ---- useIndexRoots ------------------------------------------------------------

describe("useIndexRoots", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("fetches roots from correct URL and returns list", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ roots: [mockRoot], success: true }),
    });

    const { result } = renderHook(() => useIndexRoots(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/roots"),
      expect.objectContaining({ method: "GET" }),
    );
    expect(result.current.data).toHaveLength(1);
    expect(result.current.data![0].driveId).toBe("drive-abc");
  });

  test("throws on non-ok response", async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 403, statusText: "Forbidden" });

    const { result } = renderHook(() => useIndexRoots(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeTruthy();
  });
});

// ---- useTagRules --------------------------------------------------------------

describe("useTagRules", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("fetches rules from correct URL and returns list", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ rules: [mockRule], success: true }),
    });

    const { result } = renderHook(() => useTagRules(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/rules"),
      expect.objectContaining({ method: "GET" }),
    );
    expect(result.current.data![0].tagName).toBe("produkty");
  });
});

// ---- useAddIndexRoot ----------------------------------------------------------

describe("useAddIndexRoot", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("POSTs to correct URL with input body", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true, json: async () => ({ id: 1, success: true }) }) // POST
      .mockResolvedValueOnce({ ok: true, json: async () => ({ roots: [], success: true }) }); // invalidation refetch

    const { result } = renderHook(() => useAddIndexRoot(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({
        sharePointPath: "/Fotky",
        displayName: null,
        driveId: "drive-1",
        rootItemId: "item-1",
      });
    });

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/roots"),
      expect.objectContaining({
        method: "POST",
        body: expect.stringContaining("drive-1"),
      }),
    );
  });
});

// ---- useDeleteIndexRoot -------------------------------------------------------

describe("useDeleteIndexRoot", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("DELETEs correct URL for given id", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true }) // DELETE
      .mockResolvedValueOnce({ ok: true, json: async () => ({ roots: [], success: true }) }); // invalidation refetch

    const { result } = renderHook(() => useDeleteIndexRoot(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync(7);
    });

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/roots/7"),
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

// ---- useAddTagRule ------------------------------------------------------------

describe("useAddTagRule", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("POSTs to rules endpoint with rule data", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true, json: async () => ({ id: 5, success: true }) })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ rules: [], success: true }) });

    const { result } = renderHook(() => useAddTagRule(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ pathPattern: "/Fotky/*", tagName: "fotky", sortOrder: 0 });
    });

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/rules"),
      expect.objectContaining({
        method: "POST",
        body: expect.stringContaining("fotky"),
      }),
    );
  });
});

// ---- useDeleteTagRule ---------------------------------------------------------

describe("useDeleteTagRule", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("DELETEs correct URL for given rule id", async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ rules: [], success: true }) });

    const { result } = renderHook(() => useDeleteTagRule(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync(3);
    });

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/rules/3"),
      expect.objectContaining({ method: "DELETE" }),
    );
  });
});

// ---- useReapplyTagRules -------------------------------------------------------

describe("useReapplyTagRules", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(createMockClient(mockFetch) as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("POSTs to reapply endpoint and returns photosUpdated count", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ photosUpdated: 42, success: true }),
    });

    const { result } = renderHook(() => useReapplyTagRules(), { wrapper: createWrapper() });

    let data: { photosUpdated: number } | undefined;
    await act(async () => {
      data = await result.current.mutateAsync();
    });

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/settings/rules/reapply"),
      expect.objectContaining({ method: "POST" }),
    );
    expect(data?.photosUpdated).toBe(42);
  });
});
```

- [ ] **Step 2: Run hook tests**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="usePhotobankSettings"
```

Expected: all tests pass, 0 failures.

---

## Task 6: IndexRootsTab component

**Files:**
- Create: `frontend/src/components/marketing/photobank/settings/IndexRootsTab.tsx`

- [ ] **Step 1: Create IndexRootsTab.tsx**

```tsx
import React, { useState } from "react";
import { Trash2 } from "lucide-react";
import {
  useIndexRoots,
  useAddIndexRoot,
  useDeleteIndexRoot,
} from "../../../../api/hooks/usePhotobankSettings";

const IndexRootsTab: React.FC = () => {
  const { data: roots = [], isLoading } = useIndexRoots();
  const addRoot = useAddIndexRoot();
  const deleteRoot = useDeleteIndexRoot();

  const [folderPath, setFolderPath] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [driveId, setDriveId] = useState("");
  const [rootItemId, setRootItemId] = useState("");

  const isFormValid = folderPath.trim() !== "" && driveId.trim() !== "" && rootItemId.trim() !== "";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isFormValid) return;

    await addRoot.mutateAsync({
      sharePointPath: folderPath.trim(),
      displayName: displayName.trim() || null,
      driveId: driveId.trim(),
      rootItemId: rootItemId.trim(),
    });

    setFolderPath("");
    setDisplayName("");
    setDriveId("");
    setRootItemId("");
  };

  if (isLoading) {
    return <div className="text-sm text-gray-500">Načítání...</div>;
  }

  return (
    <div className="space-y-6">
      {roots.length === 0 ? (
        <p className="text-sm text-gray-500">Žádné kořeny nejsou nakonfigurovány.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm divide-y divide-gray-200">
            <thead>
              <tr className="text-left text-xs font-semibold text-gray-500 uppercase tracking-wider">
                <th className="py-2 pr-4">Cesta</th>
                <th className="py-2 pr-4">Název</th>
                <th className="py-2 pr-4">Drive ID</th>
                <th className="py-2 pr-4">Root Item ID</th>
                <th className="py-2 pr-4">Aktivní</th>
                <th className="py-2 pr-4">Poslední indexace</th>
                <th className="py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {roots.map((root) => (
                <tr key={root.id}>
                  <td className="py-2 pr-4 font-mono text-xs">{root.sharePointPath}</td>
                  <td className="py-2 pr-4">{root.displayName ?? "—"}</td>
                  <td className="py-2 pr-4 font-mono text-xs">{root.driveId ?? "—"}</td>
                  <td className="py-2 pr-4 font-mono text-xs">{root.rootItemId ?? "—"}</td>
                  <td className="py-2 pr-4">
                    <span
                      className={`px-1.5 py-0.5 rounded text-xs ${
                        root.isActive
                          ? "bg-green-100 text-green-700"
                          : "bg-gray-100 text-gray-500"
                      }`}
                    >
                      {root.isActive ? "Ano" : "Ne"}
                    </span>
                  </td>
                  <td className="py-2 pr-4 text-xs text-gray-500">
                    {root.lastIndexedAt
                      ? new Date(root.lastIndexedAt).toLocaleDateString("cs-CZ")
                      : "Nikdy"}
                  </td>
                  <td className="py-2">
                    <button
                      onClick={() => deleteRoot.mutate(root.id)}
                      disabled={deleteRoot.isPending}
                      className="text-gray-400 hover:text-red-500 disabled:opacity-50"
                      aria-label={`Smazat kořen ${root.sharePointPath}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-3 border-t border-gray-200 pt-4">
        <h3 className="text-sm font-semibold text-gray-700">Přidat kořen</h3>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-gray-500 mb-1">Cesta *</label>
            <input
              type="text"
              value={folderPath}
              onChange={(e) => setFolderPath(e.target.value)}
              placeholder="/sites/anela/Shared Documents/Fotky"
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 mb-1">Název (volitelný)</label>
            <input
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Fotky produktů"
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 mb-1">Drive ID *</label>
            <input
              type="text"
              value={driveId}
              onChange={(e) => setDriveId(e.target.value)}
              placeholder="b!abc123..."
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 mb-1">Root Item ID *</label>
            <input
              type="text"
              value={rootItemId}
              onChange={(e) => setRootItemId(e.target.value)}
              placeholder="01ABCDEF..."
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
        </div>
        <button
          type="submit"
          disabled={addRoot.isPending || !isFormValid}
          className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {addRoot.isPending ? "Přidávám..." : "Přidat kořen"}
        </button>
      </form>
    </div>
  );
};

export default IndexRootsTab;
```

---

## Task 7: TagRulesTab component

**Files:**
- Create: `frontend/src/components/marketing/photobank/settings/TagRulesTab.tsx`

- [ ] **Step 1: Create TagRulesTab.tsx**

```tsx
import React, { useState } from "react";
import { Trash2, RefreshCw } from "lucide-react";
import {
  useTagRules,
  useAddTagRule,
  useDeleteTagRule,
  useReapplyTagRules,
} from "../../../../api/hooks/usePhotobankSettings";

const TagRulesTab: React.FC = () => {
  const { data: rules = [], isLoading } = useTagRules();
  const addRule = useAddTagRule();
  const deleteRule = useDeleteTagRule();
  const reapplyRules = useReapplyTagRules();

  const [pathPattern, setPathPattern] = useState("");
  const [tagName, setTagName] = useState("");
  const [sortOrder, setSortOrder] = useState(0);
  const [reapplyMessage, setReapplyMessage] = useState<string | null>(null);

  const sortedRules = [...rules].sort((a, b) => a.sortOrder - b.sortOrder);
  const isFormValid = pathPattern.trim() !== "" && tagName.trim() !== "";

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isFormValid) return;

    await addRule.mutateAsync({
      pathPattern: pathPattern.trim(),
      tagName: tagName.trim(),
      sortOrder,
    });

    setPathPattern("");
    setTagName("");
    setSortOrder(0);
  };

  const handleReapply = async () => {
    try {
      const result = await reapplyRules.mutateAsync();
      setReapplyMessage(`Pravidla aplikována na ${result.photosUpdated} fotek`);
    } catch {
      setReapplyMessage("Chyba při aplikaci pravidel");
    }
    setTimeout(() => setReapplyMessage(null), 5000);
  };

  if (isLoading) {
    return <div className="text-sm text-gray-500">Načítání...</div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <button
          onClick={handleReapply}
          disabled={reapplyRules.isPending}
          className="flex items-center gap-2 px-3 py-1.5 text-sm border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
          aria-label="Re-aplikovat pravidla"
        >
          <RefreshCw className={`w-4 h-4 ${reapplyRules.isPending ? "animate-spin" : ""}`} />
          Re-aplikovat pravidla
        </button>
        {reapplyMessage && (
          <span
            className={`text-sm ${
              reapplyMessage.startsWith("Chyba") ? "text-red-600" : "text-green-600"
            }`}
          >
            {reapplyMessage}
          </span>
        )}
      </div>

      {sortedRules.length === 0 ? (
        <p className="text-sm text-gray-500">Žádná pravidla nejsou nakonfigurována.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm divide-y divide-gray-200">
            <thead>
              <tr className="text-left text-xs font-semibold text-gray-500 uppercase tracking-wider">
                <th className="py-2 pr-4">Vzor cesty</th>
                <th className="py-2 pr-4">Štítek</th>
                <th className="py-2 pr-4">Pořadí</th>
                <th className="py-2 pr-4">Aktivní</th>
                <th className="py-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {sortedRules.map((rule) => (
                <tr key={rule.id}>
                  <td className="py-2 pr-4 font-mono text-xs">{rule.pathPattern}</td>
                  <td className="py-2 pr-4">{rule.tagName}</td>
                  <td className="py-2 pr-4">{rule.sortOrder}</td>
                  <td className="py-2 pr-4">
                    <span
                      className={`px-1.5 py-0.5 rounded text-xs ${
                        rule.isActive
                          ? "bg-green-100 text-green-700"
                          : "bg-gray-100 text-gray-500"
                      }`}
                    >
                      {rule.isActive ? "Ano" : "Ne"}
                    </span>
                  </td>
                  <td className="py-2">
                    <button
                      onClick={() => deleteRule.mutate(rule.id)}
                      disabled={deleteRule.isPending}
                      className="text-gray-400 hover:text-red-500 disabled:opacity-50"
                      aria-label={`Smazat pravidlo ${rule.pathPattern}`}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-3 border-t border-gray-200 pt-4">
        <h3 className="text-sm font-semibold text-gray-700">Přidat pravidlo</h3>
        <div className="grid grid-cols-3 gap-3">
          <div>
            <label className="block text-xs text-gray-500 mb-1">Vzor cesty *</label>
            <input
              type="text"
              value={pathPattern}
              onChange={(e) => setPathPattern(e.target.value)}
              placeholder="/Fotky/Produkty/*"
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 mb-1">Štítek *</label>
            <input
              type="text"
              value={tagName}
              onChange={(e) => setTagName(e.target.value)}
              placeholder="produkty"
              required
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 mb-1">Pořadí</label>
            <input
              type="number"
              value={sortOrder}
              onChange={(e) => setSortOrder(Number(e.target.value))}
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
            />
          </div>
        </div>
        <button
          type="submit"
          disabled={addRule.isPending || !isFormValid}
          className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {addRule.isPending ? "Přidávám..." : "Přidat pravidlo"}
        </button>
      </form>
    </div>
  );
};

export default TagRulesTab;
```

---

## Task 8: PhotobankSettingsPage + routing + settings link

**Files:**
- Create: `frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx`
- Modify: `frontend/src/App.tsx` (line 41 area: add import; line 398 area: add route)
- Modify: `frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx`

- [ ] **Step 1: Create PhotobankSettingsPage.tsx**

```tsx
import React, { useState } from "react";
import { Link } from "react-router-dom";
import { useMsal } from "@azure/msal-react";
import { ArrowLeft } from "lucide-react";
import IndexRootsTab from "../settings/IndexRootsTab";
import TagRulesTab from "../settings/TagRulesTab";

const ADMIN_ROLE = "administrator";
type ActiveTab = "roots" | "rules";

const TAB_CLASSES = {
  active: "border-primary-blue text-primary-blue",
  inactive: "border-transparent text-gray-500 hover:text-gray-700",
};

const PhotobankSettingsPage: React.FC = () => {
  const { accounts } = useMsal();
  const [activeTab, setActiveTab] = useState<ActiveTab>("roots");

  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;

  if (!isAdmin) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center">
          <p className="text-2xl font-semibold text-gray-700">403</p>
          <p className="text-gray-500 mt-1">Přístup odepřen</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-200 flex-shrink-0">
        <Link
          to="/marketing/photobank"
          className="text-gray-500 hover:text-gray-700"
          aria-label="Zpět na fotobanku"
        >
          <ArrowLeft className="w-4 h-4" />
        </Link>
        <h1 className="text-base font-semibold text-gray-800">Nastavení fotobanky</h1>
      </div>

      <div className="flex border-b border-gray-200 flex-shrink-0 px-4">
        <button
          onClick={() => setActiveTab("roots")}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === "roots" ? TAB_CLASSES.active : TAB_CLASSES.inactive
          }`}
        >
          Index Roots
        </button>
        <button
          onClick={() => setActiveTab("rules")}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === "rules" ? TAB_CLASSES.active : TAB_CLASSES.inactive
          }`}
        >
          Tag Rules
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-4">
        {activeTab === "roots" ? <IndexRootsTab /> : <TagRulesTab />}
      </div>
    </div>
  );
};

export default PhotobankSettingsPage;
```

- [ ] **Step 2: Add import and route in App.tsx**

In `frontend/src/App.tsx`, add import after line 41 (the existing PhotobankPage import):

```typescript
import PhotobankSettingsPage from "./components/marketing/photobank/pages/PhotobankSettingsPage";
```

Then add route after lines 395-398 (the existing `/marketing/photobank` route):

```tsx
                        <Route
                          path="/marketing/photobank/settings"
                          element={<PhotobankSettingsPage />}
                        />
```

- [ ] **Step 3: Add settings link in PhotobankPage.tsx**

`PhotobankPage.tsx` currently renders a single `<div className="flex h-full overflow-hidden">`. Wrap it to add a header bar with the settings gear for admins.

Replace the entire file content:

```tsx
import React, { useCallback, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useMsal } from "@azure/msal-react";
import { Settings } from "lucide-react";
import TagSidebar from "../TagSidebar";
import PhotoGrid from "../PhotoGrid";
import PhotoDrawer from "../PhotoDrawer";
import { usePhotos, usePhotoTags } from "../../../../api/hooks/usePhotobank";
import type { PhotoDto } from "../../../../api/hooks/usePhotobank";

const DEFAULT_PAGE_SIZE = 48;
const SIDEBAR_WIDTH = "220px";
const ADMIN_ROLE = "administrator";

const PhotobankPage: React.FC = () => {
  const { accounts } = useMsal();
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>([]);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selectedPhoto, setSelectedPhoto] = useState<PhotoDto | null>(null);

  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;

  const { data: tagsData } = usePhotoTags();

  const selectedTagNames = useMemo(
    () =>
      selectedTagIds
        .map((id) => tagsData?.find((t) => t.id === id)?.name)
        .filter((name): name is string => name !== undefined),
    [selectedTagIds, tagsData],
  );

  const { data: photosData, isLoading: photosLoading } = usePhotos({
    tags: selectedTagNames.length > 0 ? selectedTagNames : undefined,
    search: search || undefined,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });

  const handleTagToggle = useCallback((tagId: number) => {
    setSelectedTagIds((prev) =>
      prev.includes(tagId) ? prev.filter((id) => id !== tagId) : [...prev, tagId],
    );
    setPage(1);
  }, []);

  const handleSearchChange = useCallback((value: string) => {
    setSearch(value);
    setPage(1);
  }, []);

  const handleClearFilters = useCallback(() => {
    setSelectedTagIds([]);
    setSearch("");
    setPage(1);
  }, []);

  const handlePhotoSelect = useCallback((photo: PhotoDto) => {
    setSelectedPhoto((prev) => (prev?.id === photo.id ? null : photo));
  }, []);

  const handleDrawerClose = useCallback(() => {
    setSelectedPhoto(null);
  }, []);

  const handlePageChange = useCallback((newPage: number) => {
    setPage(newPage);
    setSelectedPhoto(null);
  }, []);

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {isAdmin && (
        <div className="flex justify-end px-3 py-1.5 border-b border-gray-100 flex-shrink-0">
          <Link
            to="/marketing/photobank/settings"
            className="p-1 text-gray-400 hover:text-gray-600 rounded"
            aria-label="Nastavení fotobanky"
          >
            <Settings className="w-4 h-4" />
          </Link>
        </div>
      )}
      <div className="flex flex-1 overflow-hidden">
        <div style={{ width: SIDEBAR_WIDTH }} className="flex-shrink-0">
          <TagSidebar
            tags={tagsData ?? []}
            selectedTagIds={selectedTagIds}
            search={search}
            onTagToggle={handleTagToggle}
            onSearchChange={handleSearchChange}
            onClearFilters={handleClearFilters}
          />
        </div>

        <div className="flex-1 flex overflow-hidden">
          <PhotoGrid
            photos={photosData?.items ?? []}
            selectedPhotoId={selectedPhoto?.id ?? null}
            total={photosData?.total ?? 0}
            page={photosData?.page ?? page}
            pageSize={DEFAULT_PAGE_SIZE}
            isLoading={photosLoading}
            onPhotoSelect={handlePhotoSelect}
            onPageChange={handlePageChange}
          />
        </div>

        {selectedPhoto && (
          <PhotoDrawer photo={selectedPhoto} onClose={handleDrawerClose} />
        )}
      </div>
    </div>
  );
};

export default PhotobankPage;
```

- [ ] **Step 4: Run frontend build to verify no TypeScript errors**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: `Successfully compiled` (or webpack `compiled successfully`), 0 errors.

---

## Task 9: Component tests

**Files:**
- Create: `frontend/src/components/marketing/photobank/__tests__/IndexRootsTab.test.tsx`
- Create: `frontend/src/components/marketing/photobank/__tests__/TagRulesTab.test.tsx`

- [ ] **Step 1: Create IndexRootsTab.test.tsx**

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import IndexRootsTab from "../settings/IndexRootsTab";
import * as hooks from "../../../../api/hooks/usePhotobankSettings";

jest.mock("../../../../api/hooks/usePhotobankSettings");
const mockHooks = hooks as jest.Mocked<typeof hooks>;

function renderWithQuery(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>,
  );
}

const mockRoot = {
  id: 1,
  sharePointPath: "/Fotky/Produkty",
  displayName: "Produkty",
  driveId: "drive-abc",
  rootItemId: "item-xyz",
  isActive: true,
  createdAt: "2026-01-01T00:00:00Z",
  lastIndexedAt: "2026-04-24T03:00:00Z",
};

describe("IndexRootsTab", () => {
  const mockMutateAsync = jest.fn();
  const mockMutate = jest.fn();

  beforeEach(() => {
    mockHooks.useAddIndexRoot.mockReturnValue({
      mutateAsync: mockMutateAsync.mockResolvedValue({}),
      isPending: false,
    } as any);
    mockHooks.useDeleteIndexRoot.mockReturnValue({
      mutate: mockMutate,
      isPending: false,
    } as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("shows empty state when no roots exist", () => {
    mockHooks.useIndexRoots.mockReturnValue({ data: [], isLoading: false } as any);

    renderWithQuery(<IndexRootsTab />);

    expect(screen.getByText("Žádné kořeny nejsou nakonfigurovány.")).toBeInTheDocument();
  });

  test("renders roots table with all columns", () => {
    mockHooks.useIndexRoots.mockReturnValue({ data: [mockRoot], isLoading: false } as any);

    renderWithQuery(<IndexRootsTab />);

    expect(screen.getByText("/Fotky/Produkty")).toBeInTheDocument();
    expect(screen.getByText("Produkty")).toBeInTheDocument();
    expect(screen.getByText("drive-abc")).toBeInTheDocument();
    expect(screen.getByText("item-xyz")).toBeInTheDocument();
  });

  test("shows Nikdy when lastIndexedAt is null", () => {
    mockHooks.useIndexRoots.mockReturnValue({
      data: [{ ...mockRoot, lastIndexedAt: null }],
      isLoading: false,
    } as any);

    renderWithQuery(<IndexRootsTab />);

    expect(screen.getByText("Nikdy")).toBeInTheDocument();
  });

  test("delete button calls useDeleteIndexRoot with correct id", () => {
    mockHooks.useIndexRoots.mockReturnValue({ data: [mockRoot], isLoading: false } as any);

    renderWithQuery(<IndexRootsTab />);

    const deleteBtn = screen.getByLabelText("Smazat kořen /Fotky/Produkty");
    fireEvent.click(deleteBtn);

    expect(mockMutate).toHaveBeenCalledWith(1);
  });

  test("add form submits with correct data and resets fields", async () => {
    mockHooks.useIndexRoots.mockReturnValue({ data: [], isLoading: false } as any);

    renderWithQuery(<IndexRootsTab />);

    fireEvent.change(screen.getByPlaceholderText(/sites\/anela/), {
      target: { value: "/Fotky/Test" },
    });
    fireEvent.change(screen.getByPlaceholderText("b!abc123..."), {
      target: { value: "drive-test" },
    });
    fireEvent.change(screen.getByPlaceholderText("01ABCDEF..."), {
      target: { value: "item-test" },
    });

    fireEvent.click(screen.getByText("Přidat kořen"));

    await waitFor(() =>
      expect(mockMutateAsync).toHaveBeenCalledWith({
        sharePointPath: "/Fotky/Test",
        displayName: null,
        driveId: "drive-test",
        rootItemId: "item-test",
      }),
    );
  });

  test("submit button is disabled when required fields are empty", () => {
    mockHooks.useIndexRoots.mockReturnValue({ data: [], isLoading: false } as any);

    renderWithQuery(<IndexRootsTab />);

    const submitBtn = screen.getByText("Přidat kořen");
    expect(submitBtn).toBeDisabled();
  });
});
```

- [ ] **Step 2: Create TagRulesTab.test.tsx**

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TagRulesTab from "../settings/TagRulesTab";
import * as hooks from "../../../../api/hooks/usePhotobankSettings";

jest.mock("../../../../api/hooks/usePhotobankSettings");
const mockHooks = hooks as jest.Mocked<typeof hooks>;

function renderWithQuery(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>,
  );
}

const mockRuleA = { id: 1, pathPattern: "/Fotky/A/*", tagName: "aaa", isActive: true, sortOrder: 10 };
const mockRuleB = { id: 2, pathPattern: "/Fotky/B/*", tagName: "bbb", isActive: true, sortOrder: 5 };

describe("TagRulesTab", () => {
  const mockMutateAsync = jest.fn();
  const mockMutate = jest.fn();
  const mockReapplyMutateAsync = jest.fn();

  beforeEach(() => {
    mockHooks.useAddTagRule.mockReturnValue({
      mutateAsync: mockMutateAsync.mockResolvedValue({}),
      isPending: false,
    } as any);
    mockHooks.useDeleteTagRule.mockReturnValue({
      mutate: mockMutate,
      isPending: false,
    } as any);
    mockHooks.useReapplyTagRules.mockReturnValue({
      mutateAsync: mockReapplyMutateAsync,
      isPending: false,
    } as any);
  });

  afterEach(() => jest.clearAllMocks());

  test("shows empty state when no rules exist", () => {
    mockHooks.useTagRules.mockReturnValue({ data: [], isLoading: false } as any);

    renderWithQuery(<TagRulesTab />);

    expect(screen.getByText("Žádná pravidla nejsou nakonfigurována.")).toBeInTheDocument();
  });

  test("renders rules ordered by sortOrder ascending", () => {
    mockHooks.useTagRules.mockReturnValue({
      data: [mockRuleA, mockRuleB],
      isLoading: false,
    } as any);

    renderWithQuery(<TagRulesTab />);

    const rows = screen.getAllByRole("row");
    // rows[0] = header, rows[1] = first data row (sortOrder 5 = mockRuleB), rows[2] = mockRuleA
    expect(rows[1]).toHaveTextContent("bbb");
    expect(rows[2]).toHaveTextContent("aaa");
  });

  test("delete button calls useDeleteTagRule with correct id", () => {
    mockHooks.useTagRules.mockReturnValue({ data: [mockRuleA], isLoading: false } as any);

    renderWithQuery(<TagRulesTab />);

    const deleteBtn = screen.getByLabelText("Smazat pravidlo /Fotky/A/*");
    fireEvent.click(deleteBtn);

    expect(mockMutate).toHaveBeenCalledWith(1);
  });

  test("add form submits with correct data", async () => {
    mockHooks.useTagRules.mockReturnValue({ data: [], isLoading: false } as any);

    renderWithQuery(<TagRulesTab />);

    fireEvent.change(screen.getByPlaceholderText("/Fotky/Produkty/*"), {
      target: { value: "/Fotky/Test/*" },
    });
    fireEvent.change(screen.getByPlaceholderText("produkty"), {
      target: { value: "testTag" },
    });

    fireEvent.click(screen.getByText("Přidat pravidlo"));

    await waitFor(() =>
      expect(mockMutateAsync).toHaveBeenCalledWith({
        pathPattern: "/Fotky/Test/*",
        tagName: "testTag",
        sortOrder: 0,
      }),
    );
  });

  test("reapply button shows success message with photo count", async () => {
    mockHooks.useTagRules.mockReturnValue({ data: [], isLoading: false } as any);
    mockReapplyMutateAsync.mockResolvedValue({ photosUpdated: 17 });

    renderWithQuery(<TagRulesTab />);

    fireEvent.click(screen.getByLabelText("Re-aplikovat pravidla"));

    await waitFor(() =>
      expect(screen.getByText("Pravidla aplikována na 17 fotek")).toBeInTheDocument(),
    );
  });

  test("reapply button shows error message on failure", async () => {
    mockHooks.useTagRules.mockReturnValue({ data: [], isLoading: false } as any);
    mockReapplyMutateAsync.mockRejectedValue(new Error("Server error"));

    renderWithQuery(<TagRulesTab />);

    fireEvent.click(screen.getByLabelText("Re-aplikovat pravidla"));

    await waitFor(() =>
      expect(screen.getByText("Chyba při aplikaci pravidel")).toBeInTheDocument(),
    );
  });

  test("reapply button shows spinner when pending", () => {
    mockHooks.useTagRules.mockReturnValue({ data: [], isLoading: false } as any);
    mockHooks.useReapplyTagRules.mockReturnValue({
      mutateAsync: mockReapplyMutateAsync,
      isPending: true,
    } as any);

    renderWithQuery(<TagRulesTab />);

    const icon = screen.getByLabelText("Re-aplikovat pravidla").querySelector("svg");
    expect(icon?.classList.contains("animate-spin")).toBe(true);
  });
});
```

- [ ] **Step 3: Run all component tests**

```bash
cd frontend && npm test -- --watchAll=false --testPathPattern="IndexRootsTab|TagRulesTab"
```

Expected: all tests pass, 0 failures.

- [ ] **Step 4: Run full frontend test suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: 86+ suites pass, 0 failures.

- [ ] **Step 5: Commit frontend work**

```bash
cd /path/to/worktree
git add frontend/src/api/hooks/usePhotobankSettings.ts \
        frontend/src/api/hooks/__tests__/usePhotobankSettings.test.ts \
        frontend/src/components/marketing/photobank/settings/IndexRootsTab.tsx \
        frontend/src/components/marketing/photobank/settings/TagRulesTab.tsx \
        frontend/src/components/marketing/photobank/pages/PhotobankSettingsPage.tsx \
        frontend/src/components/marketing/photobank/__tests__/IndexRootsTab.test.tsx \
        frontend/src/components/marketing/photobank/__tests__/TagRulesTab.test.tsx \
        frontend/src/components/marketing/photobank/pages/PhotobankPage.tsx \
        frontend/src/App.tsx
git commit -m "feat(photobank): admin settings page — index roots, tag rules, re-apply (#760)"
```

---

## Task 10: Final verification

- [ ] **Step 1: Run dotnet format to verify backend formatting**

```bash
cd /path/to/worktree && dotnet format backend/ --verify-no-changes
```

Expected: exits 0 (no formatting violations).

- [ ] **Step 2: Run frontend lint**

```bash
cd frontend && npm run lint
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Run full backend test suite**

```bash
dotnet test backend/
```

Expected: all tests pass, 0 failures.

- [ ] **Step 4: Run frontend build one final time**

```bash
cd frontend && npm run build 2>&1 | tail -10
```

Expected: compiled successfully, 0 errors.

- [ ] **Step 5: Push to epic branch**

```bash
cd /path/to/worktree && git push origin feat/755-photobank
```
