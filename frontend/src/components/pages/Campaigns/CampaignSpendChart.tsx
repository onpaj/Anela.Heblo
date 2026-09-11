import React from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from 'recharts';
import { DailySpend } from '../../../hooks/useCampaignDashboard';

interface CampaignSpendChartProps {
  data: DailySpend[];
  isLoading: boolean;
}

const formatCurrency = (value: number) =>
  new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(value);

export const CampaignSpendChart: React.FC<CampaignSpendChartProps> = ({
  data,
  isLoading,
}) => {
  if (isLoading) {
    return <div className="h-64 bg-gray-100 rounded animate-pulse" />;
  }

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4">
      <h3 className="text-sm font-medium text-gray-700 mb-4">Spend Over Time</h3>
      <ResponsiveContainer width="100%" height={240}>
        <LineChart data={data} margin={{ top: 4, right: 16, left: 0, bottom: 4 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis
            dataKey="date"
            tick={{ fontSize: 11 }}
            tickFormatter={(d: string) => d.slice(5)}
          />
          <YAxis tick={{ fontSize: 11 }} tickFormatter={formatCurrency} width={80} />
          <Tooltip formatter={(value: number) => formatCurrency(value)} />
          <Legend />
          <Line
            type="monotone"
            dataKey="metaSpend"
            name="Meta"
            stroke="#1877f2"
            strokeWidth={2}
            dot={false}
          />
          <Line
            type="monotone"
            dataKey="googleSpend"
            name="Google"
            stroke="#ea4335"
            strokeWidth={2}
            dot={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
};
