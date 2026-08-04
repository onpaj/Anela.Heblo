import { renderHook, waitFor, act } from "@testing-library/react";
import {
  usePhotos,
  usePhotoTags,
  useAddPhotoTag,
  useCreateTag,
  useDeleteTag,
  useBulkAddPhotoTag,
} from "../usePhotobank";
import { mockAuthenticatedApiClient, createQueryClientWrapper } from "../../testUtils";

jest.mock("../../client");

// ---- Mock data ------------------------------------------------------------

const mockPhoto = {
  id: 1,
  sharePointFileId: "file-abc",
  driveId: "drive-xyz",
  name: "photo.jpg",
  folderPath: "/Fotky/2026",
  sharePointWebUrl: "https://anela.sharepoint.com/photo.jpg",
  fileSizeBytes: 1024,
  lastModifiedAt: new Date("2026-04-01T10:00:00Z"),
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

// ---- Tests ----------------------------------------------------------------

describe("usePhotos", () => {
  let mockClient: { photobank_GetPhotos: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_GetPhotos: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("returns data from the generated client on success", async () => {
    mockClient.photobank_GetPhotos.mockResolvedValue(mockPhotosResponse);

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => usePhotos(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.total).toBe(1);
    expect(result.current.data?.items[0].name).toBe("photo.jpg");
  });

  test("passes params positionally to photobank_GetPhotos", async () => {
    mockClient.photobank_GetPhotos.mockResolvedValue(mockPhotosResponse);

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () => usePhotos({ tags: ["výroba", "marketing"], search: "foto", page: 1, pageSize: 48 }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(mockClient.photobank_GetPhotos).toHaveBeenCalledWith(
      ["výroba", "marketing"],
      "foto",
      undefined,
      undefined,
      1,
      48,
    );
  });

  test("rejects on API error", async () => {
    mockClient.photobank_GetPhotos.mockRejectedValue(new Error("Photobank API error: 500 Internal Server Error"));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => usePhotos(), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeTruthy();
  });
});

describe("usePhotoTags", () => {
  let mockClient: { photobank_GetTags: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_GetTags: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("returns tag list from photobank_GetTags", async () => {
    mockClient.photobank_GetTags.mockResolvedValue(mockTagsResponse);

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => usePhotoTags(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0].name).toBe("výroba");
    expect(result.current.data?.[0].count).toBe(5);
  });

  test("returns an empty list when tags is absent from the response", async () => {
    mockClient.photobank_GetTags.mockResolvedValue({ success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => usePhotoTags(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual([]);
  });
});

describe("useAddPhotoTag", () => {
  let mockClient: { photobank_AddPhotoTag: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_AddPhotoTag: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_AddPhotoTag with photo id and tag name body", async () => {
    mockClient.photobank_AddPhotoTag.mockResolvedValue({ tagId: 1, tagName: "výroba", success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useAddPhotoTag(42), { wrapper });

    result.current.mutate("výroba");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.photobank_AddPhotoTag).toHaveBeenCalledTimes(1);
    const [photoId, body] = mockClient.photobank_AddPhotoTag.mock.calls[0];
    expect(photoId).toBe(42);
    expect(body).toEqual(expect.objectContaining({ tagName: "výroba" }));
  });

  test("sets error state on API failure", async () => {
    mockClient.photobank_AddPhotoTag.mockRejectedValue(new Error("Photobank API error: 400 Bad Request"));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useAddPhotoTag(42), { wrapper });

    result.current.mutate("bad-tag");

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeTruthy();
  });
});

describe("useCreateTag", () => {
  let mockClient: { photobank_CreateTag: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_CreateTag: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_CreateTag with the tag name body", async () => {
    mockClient.photobank_CreateTag.mockResolvedValue({ id: 99, name: "nový tag", success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useCreateTag(), { wrapper });

    result.current.mutate("nový tag");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.photobank_CreateTag).toHaveBeenCalledTimes(1);
    const [body] = mockClient.photobank_CreateTag.mock.calls[0];
    expect(body).toEqual(expect.objectContaining({ name: "nový tag" }));
  });

  test("sets error state on API failure", async () => {
    mockClient.photobank_CreateTag.mockRejectedValue(new Error("Photobank API error: 409 Conflict"));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useCreateTag(), { wrapper });

    result.current.mutate("duplicate");

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeTruthy();
  });
});

describe("useDeleteTag", () => {
  let mockClient: { photobank_DeleteTag: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_DeleteTag: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("calls photobank_DeleteTag with the tag id", async () => {
    mockClient.photobank_DeleteTag.mockResolvedValue({ removedAssignmentCount: 0, success: true });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useDeleteTag(), { wrapper });

    result.current.mutate(55);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.photobank_DeleteTag).toHaveBeenCalledWith(55);
  });

  test("sets error state on API failure", async () => {
    mockClient.photobank_DeleteTag.mockRejectedValue(new Error("Photobank API error: 404 Not Found"));

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useDeleteTag(), { wrapper });

    result.current.mutate(999);

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.error).toBeTruthy();
  });
});

describe("useBulkAddPhotoTag", () => {
  let mockClient: { photobank_BulkAddPhotoTag: jest.Mock };

  beforeEach(() => {
    mockClient = { photobank_BulkAddPhotoTag: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  afterEach(() => jest.clearAllMocks());

  test("returns a success result on the happy path", async () => {
    mockClient.photobank_BulkAddPhotoTag.mockResolvedValue({
      success: true,
      tagId: 7,
      tagName: "výroba",
      addedCount: 12,
      alreadyTaggedCount: 3,
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useBulkAddPhotoTag(), { wrapper });

    let data;
    await act(async () => {
      data = await result.current.mutateAsync({ search: "foto", tagName: "výroba" });
    });

    expect(data).toEqual({
      success: true,
      tagId: 7,
      tagName: "výroba",
      addedCount: 12,
      alreadyTaggedCount: 3,
    });
  });

  test("translates the 2606 limit-exceeded business outcome instead of rejecting", async () => {
    mockClient.photobank_BulkAddPhotoTag.mockRejectedValue({
      success: false,
      errorCode: 2606,
      params: { Count: "12000", Limit: "5000" },
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useBulkAddPhotoTag(), { wrapper });

    let data;
    await act(async () => {
      data = await result.current.mutateAsync({ search: "foto", tagName: "výroba" });
    });

    expect(data).toEqual({
      success: false,
      errorCode: 2606,
      params: { Count: "12000", Limit: "5000" },
    });
    expect(result.current.isError).toBe(false);
  });

  test("rethrows an unrecognizable failure (e.g. 403 with no success field)", async () => {
    mockClient.photobank_BulkAddPhotoTag.mockRejectedValue({});

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useBulkAddPhotoTag(), { wrapper });

    await act(async () => {
      await expect(
        result.current.mutateAsync({ search: "foto", tagName: "výroba" }),
      ).rejects.toEqual({});
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
