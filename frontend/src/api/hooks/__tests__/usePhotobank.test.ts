import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React, { ReactNode } from "react";
import {
  usePhotos,
  usePhotoTags,
  useAddPhotoTag,
} from "../usePhotobank";
import { getAuthenticatedApiClient } from "../../client";

// Mock the API client
jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

// ---- Mock data ------------------------------------------------------------

const mockPhoto = {
  id: 1,
  sharePointFileId: "file-abc",
  driveId: "drive-xyz",
  name: "photo.jpg",
  folderPath: "/Fotky/2026",
  sharePointWebUrl: "https://anela.sharepoint.com/photo.jpg",
  fileSizeBytes: 1024,
  lastModifiedAt: "2026-04-01T10:00:00Z",
  tags: [{ id: 10, name: "výroba", source: "Rule" }],
};

const mockPhotosResponse = {
  items: [mockPhoto],
  total: 1,
  page: 1,
  pageSize: 48,
};

const mockTagsResponse = {
  tags: [
    { id: 10, name: "výroba", count: 5 },
    { id: 11, name: "marketing", count: 3 },
  ],
  success: true,
};

// ---- Helpers --------------------------------------------------------------

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

// ---- Tests ----------------------------------------------------------------

describe("usePhotos", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(
      createMockClient(mockFetch) as any,
    );
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test("returns data from API on success", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => mockPhotosResponse,
    });

    // Act
    const { result } = renderHook(() => usePhotos(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Assert
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.total).toBe(1);
    expect(result.current.data?.items[0].name).toBe("photo.jpg");
  });

  test("passes search param in query string", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => mockPhotosResponse,
    });

    // Act
    const { result } = renderHook(
      () => usePhotos({ search: "foto", page: 1, pageSize: 48 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    // Assert
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("search=foto"),
      expect.any(Object),
    );
  });

  test("passes tag filter in query string", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => mockPhotosResponse,
    });

    // Act
    const { result } = renderHook(
      () => usePhotos({ tags: ["výroba", "marketing"] }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    // Assert
    const calledUrl: string = mockFetch.mock.calls[0][0];
    expect(calledUrl).toContain("tags=v%C3%BDroba");
    expect(calledUrl).toContain("tags=marketing");
  });

  test("throws on non-ok response", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
      statusText: "Internal Server Error",
    });

    // Act
    const { result } = renderHook(() => usePhotos(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Assert
    expect(result.current.error).toBeTruthy();
  });
});

describe("usePhotoTags", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(
      createMockClient(mockFetch) as any,
    );
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test("returns tag list from API", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => mockTagsResponse,
    });

    // Act
    const { result } = renderHook(() => usePhotoTags(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Assert
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0].name).toBe("výroba");
    expect(result.current.data?.[0].count).toBe(5);
  });

  test("calls the /api/photobank/tags endpoint", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => mockTagsResponse,
    });

    // Act
    const { result } = renderHook(() => usePhotoTags(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    // Assert
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/tags"),
      expect.any(Object),
    );
  });
});

describe("useAddPhotoTag", () => {
  let mockFetch: jest.Mock;

  beforeEach(() => {
    mockFetch = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue(
      createMockClient(mockFetch) as any,
    );
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  test("calls POST to correct endpoint with tag name", async () => {
    // Arrange
    mockFetch.mockResolvedValue({ ok: true });

    // Act
    const { result } = renderHook(() => useAddPhotoTag(42), {
      wrapper: createWrapper(),
    });

    result.current.mutate("výroba");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Assert
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/photobank/photos/42/tags"),
      expect.objectContaining({ method: "POST" }),
    );
  });

  test("sets error state on API failure", async () => {
    // Arrange
    mockFetch.mockResolvedValue({
      ok: false,
      status: 400,
      statusText: "Bad Request",
    });

    // Act
    const { result } = renderHook(() => useAddPhotoTag(42), {
      wrapper: createWrapper(),
    });

    result.current.mutate("bad-tag");

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Assert
    expect(result.current.error).toBeTruthy();
  });
});
