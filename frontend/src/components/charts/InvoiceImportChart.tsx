import React from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
} from 'recharts';
import { format, parseISO } from 'date-fns';
import { cs } from 'date-fns/locale';
import { DailyInvoiceCount } from '../../api/hooks/useInvoiceImportStatistics';

interface InvoiceImportChartProps {
  data: DailyInvoiceCount[];
  minimumThreshold: number;
  dateType: 'InvoiceDate' | 'LastSyncTime';
}

interface ChartDataPoint {
  date: string;
  displayDate: string;
  count: number;
  isBelowThreshold: boolean;
}

/**
 * Line chart component for displaying invoice import statistics
 */
export const InvoiceImportChart: React.FC<InvoiceImportChartProps> = ({
  data,
  minimumThreshold,
  dateType,
}) => {
  // Transform data for chart
  const chartData: ChartDataPoint[] = data.map((item) => ({
    date: item.date,
    displayDate: format(parseISO(item.date), 'dd.MM.', { locale: cs }),
    count: item.count,
    isBelowThreshold: item.isBelowThreshold,
  }));

  // Custom tooltip component
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      const fullDate = format(parseISO(data.date), 'dd. MMMM yyyy', { locale: cs });
      
      return (
        <div className="bg-white p-3 border border-gray-200 rounded-lg shadow-lg">
          <p className="font-medium text-gray-900">{fullDate}</p>
          <p className="text-sm text-gray-600">
            Počet faktur: <span className="font-medium">{data.count}</span>
          </p>
          {data.isBelowThreshold && (
            <p className="text-sm text-red-600 font-medium">
              ⚠️ Pod minimálním prahem ({minimumThreshold})
            </p>
          )}
        </div>
      );
    }
    return null;
  };

  // Custom dot component to highlight problematic days
  const CustomDot = (props: any) => {
    const { cx, cy, payload } = props;
    if (payload.isBelowThreshold) {
      return (
        <circle
          cx={cx}
          cy={cy}
          r={4}
          fill="#dc2626"
          stroke="#fff"
          strokeWidth={2}
        />
      );
    }
    return null;
  };

  return (
    <div className="w-full">
      {/* Chart title and info */}
      <div className="mb-4">
        <h3 className="text-lg font-medium text-gray-900 mb-1">
          Import vydaných faktur - posledních 14 dní
        </h3>
        <p className="text-sm text-gray-600">
          Zobrazení podle: {dateType === 'InvoiceDate' ? 'datum vystavení faktury' : 'datum importu faktury'}
        </p>
        <p className="text-sm text-gray-600">
          Minimální prah: <span className="font-medium">{minimumThreshold} faktur/den</span>
        </p>
      </div>

      {/* Chart container */}
      <div className="h-80 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={chartData} margin={{ top: 20, right: 30, left: 20, bottom: 20 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
            <XAxis
              dataKey="displayDate"
              tick={{ fontSize: 12 }}
              stroke="#6b7280"
            />
            <YAxis
              tick={{ fontSize: 12 }}
              stroke="#6b7280"
              label={{ value: 'Počet faktur', angle: -90, position: 'insideLeft' }}
            />
            <Tooltip content={<CustomTooltip />} />
            
            {/* Reference line for minimum threshold */}
            <ReferenceLine
              y={minimumThreshold}
              stroke="#dc2626"
              strokeDasharray="5 5"
              strokeWidth={2}
              label={{ 
                value: `Min. prah (${minimumThreshold})`, 
                position: 'top',
                style: { fill: '#dc2626', fontSize: '12px' }
              }}
            />
            
            {/* Main data line */}
            <Line
              type="monotone"
              dataKey="count"
              stroke="#3b82f6"
              strokeWidth={2}
              dot={<CustomDot />}
              activeDot={{ r: 6, fill: '#3b82f6' }}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>

      {/* Legend */}
      <div className="mt-4 flex flex-wrap gap-4 text-sm">
        <div className="flex items-center gap-2">
          <div className="w-4 h-0.5 bg-blue-500"></div>
          <span className="text-gray-600">Počet faktur</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-4 h-0.5 bg-red-600 border-dashed border-t-2"></div>
          <span className="text-gray-600">Minimální prah</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-3 h-3 bg-red-600 rounded-full border-2 border-white"></div>
          <span className="text-gray-600">Problémové dny</span>
        </div>
      </div>
    </div>
  );
};