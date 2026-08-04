import { renderHook, waitFor, act } from "@testing-library/react";
import {
  useIndexRoots,
  useTagRules,
  useAddIndexRoot,
  useDeleteIndexRoot,
  useAddTagRule,
  useDeleteTagRule,
  useReapplyTagRules,
} from "../usePhotobankSettings";
import { mockAuthenticatedApiClient, createQueryClientWrapper } from "../../testUtils";

jest.mock("../../client");

// ---- Mock data ----------------------------------------------------------------

const mockRoot = {
  id: 1,
  sharePointPath: "/Fotky/Produkty",
  displayName: "Produkty",
  driveId: "drive-abc",
  rootItemId: "item-xyz",
  isActive: true,
  createdAt: new Date("2026-01-01T00:00:00Z"),
  lastIndexedAt: new Date("2026-04-24T03:00:00Z"),
};

const mockRule = {
  id: 1,
  pathPattern: "/Fotky/Produkty/*",
  tagName: "produkty",
  isActive: true,
  sortOrder: 10,
};

// ---- useIndexRoots ------------------------------------------------------------

describe("useIndexRoots", () => {
  let mockClient: { photobank_GetRoots: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_GetRoots: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("returns roots from photobank_GetRoots", async () => {
    mockClient.photobank_GetRoots.mockResolvedValue({ roots: [mockRoot], success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useIndexRoots(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.photobank_GetRoots).toHaveBeenCalledTimes(1);
    expect(result.current.data).toHaveLength(1);
    expect(result.current.data![0].driveId).toBe("drive-abc");
  });

  test("rejects on API error", async () => {
    mockClient.photobank_GetRoots.mockRejectedValue(new Error("Photobank settings API error: 403 Forbidden"));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useIndexRoots(), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeTruthy();
  });
});

// ---- useTagRules --------------------------------------------------------------

describe("useTagRules", () => {
  let mockClient: { photobank_GetRules: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_GetRules: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("returns rules from photobank_GetRules", async () => {
    mockClient.photobank_GetRules.mockResolvedValue({ rules: [mockRule], success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useTagRules(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data![0].tagName).toBe("produkty");
  });
});

// ---- useAddIndexRoot ----------------------------------------------------------

describe("useAddIndexRoot", () => {
  let mockClient: { photobank_AddRoot: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_AddRoot: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_AddRoot with the input body, normalizing null displayName to undefined", async () => {
    mockClient.photobank_AddRoot.mockResolvedValue({ id: 1, success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useAddIndexRoot(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        sharePointPath: "/Fotky",
        displayName: null,
        driveId: "drive-1",
      });
    });

    expect(mockClient.photobank_AddRoot).toHaveBeenCalledTimes(1);
    const [body] = mockClient.photobank_AddRoot.mock.calls[0];
    expect(body).toEqual(
      expect.objectContaining({
        sharePointPath: "/Fotky",
        displayName: undefined,
        driveId: "drive-1",
      }),
    );
  });
});

// ---- useDeleteIndexRoot -------------------------------------------------------

describe("useDeleteIndexRoot", () => {
  let mockClient: { photobank_DeleteRoot: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_DeleteRoot: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_DeleteRoot with the given id", async () => {
    mockClient.photobank_DeleteRoot.mockResolvedValue({ success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useDeleteIndexRoot(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync(7);
    });

    expect(mockClient.photobank_DeleteRoot).toHaveBeenCalledWith(7);
  });
});

// ---- useAddTagRule ------------------------------------------------------------

describe("useAddTagRule", () => {
  let mockClient: { photobank_AddRule: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_AddRule: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_AddRule with the rule data", async () => {
    mockClient.photobank_AddRule.mockResolvedValue({ id: 5, success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useAddTagRule(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({ pathPattern: "/Fotky/*", tagName: "fotky", sortOrder: 0 });
    });

    expect(mockClient.photobank_AddRule).toHaveBeenCalledTimes(1);
    const [body] = mockClient.photobank_AddRule.mock.calls[0];
    expect(body).toEqual(
      expect.objectContaining({ pathPattern: "/Fotky/*", tagName: "fotky", sortOrder: 0 }),
    );
  });
});

// ---- useDeleteTagRule ---------------------------------------------------------

describe("useDeleteTagRule", () => {
  let mockClient: { photobank_DeleteRule: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_DeleteRule: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_DeleteRule with the given rule id", async () => {
    mockClient.photobank_DeleteRule.mockResolvedValue({ success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useDeleteTagRule(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync(3);
    });

    expect(mockClient.photobank_DeleteRule).toHaveBeenCalledWith(3);
  });
});

// ---- useReapplyTagRules -------------------------------------------------------

describe("useReapplyTagRules", () => {
  let mockClient: { photobank_ReapplyRules: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_ReapplyRules: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_ReapplyRules and returns photosUpdated count", async () => {
    mockClient.photobank_ReapplyRules.mockResolvedValue({ photosUpdated: 42, success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useReapplyTagRules(), { wrapper });

    let data: { photosUpdated?: number } | undefined;
    await act(async () => {
      data = await result.current.mutateAsync();
    });

    expect(mockClient.photobank_ReapplyRules).toHaveBeenCalledTimes(1);
    expect(data?.photosUpdated).toBe(42);
  });
});
