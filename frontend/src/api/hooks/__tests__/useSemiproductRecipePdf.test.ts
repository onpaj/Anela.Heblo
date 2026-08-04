import { renderHook, act } from "@testing-library/react";
import { useSemiproductRecipePdf } from "../useSemiproductRecipePdf";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client");
const mockGetAuthenticatedApiClient =
  getAuthenticatedApiClient as jest.MockedFunction<
    typeof getAuthenticatedApiClient
  >;

describe("useSemiproductRecipePdf", () => {
  let mockApiClient: any;

  beforeEach(() => {
    mockApiClient = {
      manufactureBatch_GetRecipePdf: jest.fn(),
    };
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient);

    URL.createObjectURL = jest.fn().mockReturnValue("blob:mock-url");
    URL.revokeObjectURL = jest.fn();
    window.open = jest.fn();
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.clearAllMocks();
    jest.useRealTimers();
  });

  test("calls the generated typed client method, not raw fetch", async () => {
    const mockBlob = new Blob(["pdf"], { type: "application/pdf" });
    mockApiClient.manufactureBatch_GetRecipePdf.mockResolvedValueOnce({
      data: mockBlob,
      status: 200,
    });

    const { result } = renderHook(() => useSemiproductRecipePdf());

    await act(async () => {
      await result.current.openRecipePdf("PROD001", 50);
    });

    expect(mockApiClient.manufactureBatch_GetRecipePdf).toHaveBeenCalledWith(
      "PROD001",
      50,
    );
    expect(mockApiClient.http).toBeUndefined();
  });

  test("passes batchSize through as undefined when not provided", async () => {
    mockApiClient.manufactureBatch_GetRecipePdf.mockResolvedValueOnce({
      data: new Blob(["pdf"]),
      status: 200,
    });

    const { result } = renderHook(() => useSemiproductRecipePdf());

    await act(async () => {
      await result.current.openRecipePdf("PROD001");
    });

    expect(mockApiClient.manufactureBatch_GetRecipePdf).toHaveBeenCalledWith(
      "PROD001",
      undefined,
    );
  });

  test("opens the blob URL in a new tab", async () => {
    const mockBlob = new Blob(["pdf"], { type: "application/pdf" });
    mockApiClient.manufactureBatch_GetRecipePdf.mockResolvedValueOnce({
      data: mockBlob,
      status: 200,
    });

    const { result } = renderHook(() => useSemiproductRecipePdf());

    await act(async () => {
      await result.current.openRecipePdf("PROD001");
    });

    expect(URL.createObjectURL).toHaveBeenCalledWith(mockBlob);
    expect(window.open).toHaveBeenCalledWith(
      "blob:mock-url",
      "_blank",
      "noopener,noreferrer",
    );
  });

  test("schedules URL revocation after 10 seconds", async () => {
    mockApiClient.manufactureBatch_GetRecipePdf.mockResolvedValueOnce({
      data: new Blob(["pdf"]),
      status: 200,
    });

    const { result } = renderHook(() => useSemiproductRecipePdf());

    await act(async () => {
      await result.current.openRecipePdf("PROD001");
    });

    expect(URL.revokeObjectURL).not.toHaveBeenCalled();

    act(() => {
      jest.advanceTimersByTime(10000);
    });

    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mock-url");
  });

  test("sets isLoading to true during the request and false after", async () => {
    let resolveResponse: (r: any) => void;
    const responsePromise = new Promise((res) => {
      resolveResponse = res;
    });
    mockApiClient.manufactureBatch_GetRecipePdf.mockReturnValueOnce(
      responsePromise,
    );

    const { result } = renderHook(() => useSemiproductRecipePdf());

    expect(result.current.isLoading).toBe(false);

    const openPromise = act(async () => {
      await result.current.openRecipePdf("PROD001");
    });

    resolveResponse!({ data: new Blob(["pdf"]), status: 200 });
    await openPromise;

    expect(result.current.isLoading).toBe(false);
  });

  test("sets error when the generated client throws (non-2xx)", async () => {
    mockApiClient.manufactureBatch_GetRecipePdf.mockRejectedValueOnce(
      new Error("An unexpected server error occurred."),
    );

    const { result } = renderHook(() => useSemiproductRecipePdf());

    await act(async () => {
      await result.current.openRecipePdf("MISSING");
    });

    expect(result.current.error?.message).toBe(
      "An unexpected server error occurred.",
    );
    expect(window.open).not.toHaveBeenCalled();
  });
});
