import { useState, useEffect, useCallback } from 'react';
import { getAuthenticatedApiClient } from '../api/client';

export interface DailySpend {
  date: string;
  metaSpend: number;
  googleSpend: number;
}

export interface CampaignDashboard {
  totalSpend: number;
  totalConversions: number;
  avgRoas: number;
  avgCpc: number;
  spendOverTime: DailySpend[];
}

export type AdPlatformFilter = 'Meta' | 'Google' | undefined;

export function useCampaignDashboard(
  from: string,
  to: string,
  platform: AdPlatformFilter
) {
  const [data, setData] = useState<CampaignDashboard | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchDashboard = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const params = new URLSearchParams({ from, to });
      if (platform) params.append('platform', platform);

      const relativeUrl = `/api/campaigns/dashboard?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const json = await response.json();
      setData(json);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load dashboard');
    } finally {
      setIsLoading(false);
    }
  }, [from, to, platform]);

  useEffect(() => {
    fetchDashboard();
  }, [fetchDashboard]);

  return { data, isLoading, error, refetch: fetchDashboard };
}
