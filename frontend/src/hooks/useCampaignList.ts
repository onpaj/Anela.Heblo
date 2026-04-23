import { useState, useEffect, useCallback } from 'react';
import { getAuthenticatedApiClient } from '../api/client';
import { AdPlatformFilter } from './useCampaignDashboard';

export interface CampaignSummary {
  id: string;
  name: string;
  platform: 'Meta' | 'Google';
  status: string;
  spend: number;
  impressions: number;
  clicks: number;
  conversions: number;
  roas: number;
}

export function useCampaignList(
  from: string,
  to: string,
  platform: AdPlatformFilter
) {
  const [campaigns, setCampaigns] = useState<CampaignSummary[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchCampaigns = useCallback(async () => {
    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const params = new URLSearchParams({ from, to });
      if (platform) params.append('platform', platform);

      const relativeUrl = `/api/campaigns?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const json = await response.json();
      setCampaigns(json);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load campaigns');
    } finally {
      setIsLoading(false);
    }
  }, [from, to, platform]);

  useEffect(() => {
    fetchCampaigns();
  }, [fetchCampaigns]);

  return { campaigns, isLoading, error, refetch: fetchCampaigns };
}
