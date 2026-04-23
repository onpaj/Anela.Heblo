import React from 'react';
import { AdSetDetail } from '../../../hooks/useCampaignDetail';

interface CampaignAdSetRowProps {
  adSet: AdSetDetail;
}

const formatCurrency = (v: number) =>
  new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(v);

const formatNumber = (v: number) =>
  new Intl.NumberFormat('cs-CZ').format(v);

export const CampaignAdSetRow: React.FC<CampaignAdSetRowProps> = ({ adSet }) => {
  const [expanded, setExpanded] = React.useState(false);

  return (
    <>
      <tr
        className="bg-gray-50 cursor-pointer hover:bg-gray-100"
        onClick={() => setExpanded(e => !e)}
      >
        <td className="px-4 py-2 pl-8 text-sm font-medium text-gray-700" colSpan={2}>
          {expanded ? '▾' : '▸'} {adSet.name}
        </td>
        <td className="px-4 py-2 text-sm text-gray-500">{adSet.status}</td>
        <td className="px-4 py-2 text-sm text-gray-500" colSpan={5}>
          {adSet.ads.length} ads
        </td>
      </tr>
      {expanded &&
        adSet.ads.map(ad => (
          <tr key={ad.id} className="bg-gray-50/50">
            <td className="px-4 py-2 pl-12 text-sm text-gray-600" colSpan={2}>
              {ad.creativePreviewUrl ? (
                <a
                  href={ad.creativePreviewUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="underline"
                >
                  {ad.name}
                </a>
              ) : (
                ad.name
              )}
            </td>
            <td className="px-4 py-2 text-xs text-gray-400">{ad.status}</td>
            <td className="px-4 py-2 text-sm text-right">{formatCurrency(ad.spend)}</td>
            <td className="px-4 py-2 text-sm text-right">{formatNumber(ad.impressions)}</td>
            <td className="px-4 py-2 text-sm text-right">{formatNumber(ad.clicks)}</td>
            <td className="px-4 py-2 text-sm text-right">{ad.conversions}</td>
            <td className="px-4 py-2 text-sm text-right">—</td>
          </tr>
        ))}
    </>
  );
};
