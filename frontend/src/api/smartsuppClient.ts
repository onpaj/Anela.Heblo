import { getAuthenticatedApiClient } from "./client";

type ApiClientInternal = {
  baseUrl: string;
  http: { fetch: (url: string, init: RequestInit) => Promise<Response> };
};

function asInternal(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
): ApiClientInternal {
  return apiClient as unknown as ApiClientInternal;
}

export function getClientAndBaseUrl(): {
  apiClient: ReturnType<typeof getAuthenticatedApiClient>;
  baseUrl: string;
} {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = asInternal(apiClient).baseUrl;
  return { apiClient, baseUrl };
}

export async function apiGet(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
): Promise<Response> {
  return asInternal(apiClient).http.fetch(url, { method: "GET" });
}

export async function apiPost(
  apiClient: ReturnType<typeof getAuthenticatedApiClient>,
  url: string,
  body: unknown,
): Promise<Response> {
  return asInternal(apiClient).http.fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}
