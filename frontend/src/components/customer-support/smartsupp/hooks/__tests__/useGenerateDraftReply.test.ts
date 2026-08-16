import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useGenerateDraftReply } from "../useGenerateDraftReply";
import { getAuthenticatedApiClient } from "../../../../../api/client";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGenerateDraftReply = jest.fn();

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockGenerateDraftReply.mockReset();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    smartsupp_GenerateDraftReply: mockGenerateDraftReply,
  });
});

describe("useGenerateDraftReply", () => {
  it("returns answer and sources on success", async () => {
    mockGenerateDraftReply.mockResolvedValue({
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

  it("passes the topic through the typed client", async () => {
    mockGenerateDraftReply.mockResolvedValue({ success: true, answer: "x", sources: [] });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Reklamace"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(mockGenerateDraftReply).toHaveBeenCalledWith(
      "c1",
      expect.objectContaining({ topic: "Reklamace" }),
    );
  });

  it("surfaces a Czech message for a known error code on the typed response", async () => {
    mockGenerateDraftReply.mockResolvedValue({
      success: false,
      errorCode: "SmartsuppDraftReplyAiUnavailable",
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/nedostupná/i);
  });

  it("surfaces a generic message when the call throws (untyped 400/404/503)", async () => {
    mockGenerateDraftReply.mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo se/i);
  });
});
