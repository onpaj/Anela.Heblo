import { useState, useCallback } from 'react';
import { getAuthenticatedApiClient } from '../api/client';

export interface AdDetail {
  id: string;
  name: string;
  status: string;
  creativePreviewUrl?: string;
  spend: number;
  impressions: number;
  clicks: number;
  conversions: number;
}

export interface AdSetDetail {
  id: string;
  name: string;
  status: string;
  ads: AdDetail[];
}

export interface CampaignDetail {
  id: string;
  name: string;
  platform: 'Meta' | 'Google';
  status: string;
  adSets: AdSetDetail[];
}

export function useCampaignDetail() {
  const [details, setDetails] = useState<Record<string, CampaignDetail>>({});
  const [loadingIds, setLoadingIds] = useState<Set<string>>(new Set());

  const fetchDetail = useCallback(async (campaignId: string, from: string, to: string) => {
    if (details[campaignId]) return; // already loaded

    setLoadingIds(prev => new Set(prev).add(campaignId));

    try {
      const apiClient = await getAuthenticatedApiClient();
      const params = new URLSearchParams({ from, to });
      const relativeUrl = `/api/campaigns/${campaignId}?${params.toString()}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });

      if (!response.ok) return;

      const json: CampaignDetail = await response.json();
      setDetails(prev => ({ ...prev, [campaignId]: json }));
    } finally {
      setLoadingIds(prev => {
        const next = new Set(prev);
        next.delete(campaignId);
        return next;
      });
    }
  }, [details]);

  return { details, loadingIds, fetchDetail };
}
