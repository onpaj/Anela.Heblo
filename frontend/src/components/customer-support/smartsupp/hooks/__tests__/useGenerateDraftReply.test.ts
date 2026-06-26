import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useGenerateDraftReply } from "../useGenerateDraftReply";
import { getAuthenticatedApiClient } from "../../../../../api/client";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();

function setApiResponse(status: number, body: unknown): void {
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    baseUrl: "http://api.test",
    http: { fetch: mockFetch },
  });
  mockFetch.mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  });
}

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockFetch.mockReset();
});

describe("useGenerateDraftReply", () => {
  it("returns answer and sources on success", async () => {
    setApiResponse(200, {
      success: true,
      answer: "Dobrý den, balíky odesíláme do 24 hodin.",
      sources: [{ documentId: "d1", filename: "doprava.pdf", excerpt: "...", score: 0.9 }],
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Doprava"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(result.current.result!.answer).toMatch(/balíky odesíláme/);
    expect(result.current.result!.sources).toHaveLength(1);
  });

  it("posts the topic in the request body", async () => {
    setApiResponse(200, { success: true, answer: "x", sources: [] });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Reklamace"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(mockFetch).toHaveBeenCalledWith(
      "http://api.test/api/smartsupp/conversations/c1/draft-reply",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ topic: "Reklamace" }) }),
    );
  });

  it("surfaces a Czech message for a known error code", async () => {
    setApiResponse(503, { success: false, errorCode: "SmartsuppDraftReplyAiUnavailable" });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/nedostupná/i);
  });

  it("surfaces a generic message for an unknown failure", async () => {
    setApiResponse(500, { success: false });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo se/i);
  });
});
