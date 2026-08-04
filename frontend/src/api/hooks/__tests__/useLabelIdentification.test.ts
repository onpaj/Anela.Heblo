import { renderHook, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useIdentifyLabelMutation } from "../useLabelIdentification";
import { getAuthenticatedApiClient } from "../../client";

jest.mock("../../client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const wrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe("useIdentifyLabelMutation", () => {
  it("sends the photo as a FileParameter and returns the response", async () => {
    const identify = jest.fn().mockResolvedValue({
      success: true,
      decision: "Auto",
      rawText: "Tocopherol",
      candidates: [{ family: "KRE005", score: 100, variants: [] }],
    });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      labelIdentification_Identify: identify,
    });

    const { result } = renderHook(() => useIdentifyLabelMutation(), { wrapper });
    const file = new File(["x"], "label.jpg", { type: "image/jpeg" });
    const response = await result.current.mutateAsync(file);

    await waitFor(() => expect(identify).toHaveBeenCalledTimes(1));
    expect(identify).toHaveBeenCalledWith({ data: file, fileName: "label.jpg" });
    expect(response.candidates[0].family).toBe("KRE005");
  });

  it("propagates API errors so the screen can show a Czech message", async () => {
    const identify = jest.fn().mockRejectedValue(new Error("boom"));
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      labelIdentification_Identify: identify,
    });

    const { result } = renderHook(() => useIdentifyLabelMutation(), { wrapper });
    const file = new File(["x"], "label.jpg", { type: "image/jpeg" });

    await expect(result.current.mutateAsync(file)).rejects.toThrow("boom");
  });
});
