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
      .mockResolvedValueOnce({ ok: true, json: async () => ({ id: 1, success: true }) })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ roots: [], success: true }) });

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
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ roots: [], success: true }) });

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
