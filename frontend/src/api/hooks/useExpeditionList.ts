import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

export const useRunExpeditionListPrintFix = () => {
  return useMutation({
    mutationFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const relativeUrl = `/api/expedition-list/run-fix`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        throw new Error(
          errorData?.errorMessage ?? `HTTP error! status: ${response.status}`
        );
      }

      return await response.json();
    },
  });
};
