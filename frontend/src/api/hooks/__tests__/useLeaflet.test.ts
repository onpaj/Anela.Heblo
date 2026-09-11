import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useGenerateLeafletMutation, useSubmitLeafletFeedbackMutation } from "../useLeaflet";
import * as clientModule from "../../client";
import { AudienceType, ErrorCodes, GenerateLeafletResponse, LeafletLength } from "../../generated/api-client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { leaflet: ["leaflet"] },
}));

const mockGetClient = clientModule.getAuthenticatedApiClient as jest.MockedFunction<
  typeof clientModule.getAuthenticatedApiClient
>;

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

const setFetch = (response: Partial<Response> & { json: () => Promise<unknown> }) => {
  const fetchMock = jest.fn().mockResolvedValue(response);
  mockGetClient.mockReturnValue({
    baseUrl: "http://test",
    http: { fetch: fetchMock },
  } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);
  return fetchMock;
};

const feedbackParams = {
  generationId: "gen-1",
  precisionScore: 4,
  styleScore: 5,
  comment: "looks good",
};

describe("useSubmitLeafletFeedbackMutation", () => {
  it("returns { success: false, alreadySubmitted: true } without throwing on HTTP 409", async () => {
    setFetch({ ok: false, status: 409, json: async () => ({}) });

    const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
      wrapper: createWrapper,
    });

    const res = await result.current.mutateAsync(feedbackParams);

    expect(res).toEqual({ success: false, alreadySubmitted: true });
  });

  it("throws with the status code in the message on a non-ok, non-409 response", async () => {
    setFetch({ ok: false, status: 500, json: async () => ({}) });

    const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync(feedbackParams)).rejects.toThrow(
      "Submit feedback failed: 500",
    );
  });

  it("returns the parsed JSON body on an ok response", async () => {
    const body = { success: true, errorCode: null, alreadySubmitted: false };
    setFetch({ ok: true, json: async () => body });

    const { result } = renderHook(() => useSubmitLeafletFeedbackMutation(), {
      wrapper: createWrapper,
    });

    const res = await result.current.mutateAsync(feedbackParams);

    await waitFor(() => expect(res).toEqual(body));
  });
});

describe("useGenerateLeafletMutation", () => {
  const generateParams = {
    topic: "Bisabolol",
    audience: AudienceType.EndConsumer,
    length: LeafletLength.Medium,
  };

  it("resolves with the GenerateLeafletResponse returned by leaflet_Generate", async () => {
    const successResponse = new GenerateLeafletResponse({
      success: true,
      content: "Generated leaflet content",
      id: "gen-1",
      kbSourceCount: 3,
      leafletSourceCount: 2,
    });
    const mockLeafletGenerate = jest.fn().mockResolvedValue(successResponse);
    mockGetClient.mockReturnValue({
      leaflet_Generate: mockLeafletGenerate,
    } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);

    const { result } = renderHook(() => useGenerateLeafletMutation(), {
      wrapper: createWrapper,
    });

    const res = await result.current.mutateAsync(generateParams);

    expect(mockLeafletGenerate).toHaveBeenCalledTimes(1);
    expect(res).toBe(successResponse);
  });

  it("propagates a rejected GenerateLeafletResponse (422) unchanged out of mutateAsync", async () => {
    const errorResponse = new GenerateLeafletResponse({
      success: false,
      errorCode: ErrorCodes.LeafletEmptyRetrieval,
    });
    const mockLeafletGenerate = jest.fn().mockRejectedValue(errorResponse);
    mockGetClient.mockReturnValue({
      leaflet_Generate: mockLeafletGenerate,
    } as unknown as ReturnType<typeof clientModule.getAuthenticatedApiClient>);

    const { result } = renderHook(() => useGenerateLeafletMutation(), {
      wrapper: createWrapper,
    });

    await expect(result.current.mutateAsync(generateParams)).rejects.toBe(errorResponse);
  });
});
