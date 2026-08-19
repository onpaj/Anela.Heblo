import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { useSubmitLeafletFeedbackMutation } from "../useLeaflet";
import * as clientModule from "../../client";

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
