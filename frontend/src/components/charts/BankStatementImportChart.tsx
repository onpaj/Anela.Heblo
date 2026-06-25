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
  ReferenceArea,
} from 'recharts';
import { format, isWeekend } from 'date-fns';
import { cs } from 'date-fns/locale';
import { DailyBankStatementStatistics } from '../../api/hooks/useBankStatements';

interface BankStatementImportChartProps {
  data: DailyBankStatementStatistics[];
  viewType?: 'ImportCount' | 'TotalItemCount';
  dateType?: 'ImportDate' | 'StatementDate';
  minimumThreshold: number;
}

interface ChartDataPoint {
  date: Date;
  displayDate: string;
  count: number;
  itemCount: number;
  isBelowThreshold: boolean;
  isWeekend: boolean;
}

/**
 * Line chart component for displaying bank statement import statistics
 */
export const BankStatementImportChart: React.FC<BankStatementImportChartProps> = ({
  data,
  viewType = 'ImportCount',
  dateType = 'ImportDate',
  minimumThreshold,
}) => {
  // Transform data for chart
  const chartData: ChartDataPoint[] = data.map((item) => {
    const isWeekendDay = isWeekend(item.date!);
    const currentCount = viewType === 'ImportCount' ? item.importCount : item.totalItemCount;

    return {
      date: item.date!,
      displayDate: format(item.date!, 'dd.MM.', { locale: cs }),
      count: item.importCount,
      itemCount: item.totalItemCount,
      isBelowThreshold: currentCount < minimumThreshold,
      isWeekend: isWeekendDay,
    };
  });

  // Debug: log weekend data
  console.log('Bank statements chart data:', chartData.map(d => ({ 
    date: d.displayDate, 
    isWeekend: d.isWeekend,
    dayOfWeek: format(d.date, 'EEEE', { locale: cs })
  })));

  // Custom tooltip component
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      const fullDate = format(data.date, 'dd. MMMM yyyy', { locale: cs });
      const dayOfWeek = format(data.date, 'EEEE', { locale: cs });
      
      return (
        <div className="bg-white p-3 border border-gray-200 rounded-lg shadow-lg">
          <p className="font-medium text-gray-900">{fullDate}</p>
          <p className="text-xs text-gray-500 capitalize">{dayOfWeek}</p>
          {viewType === 'ImportCount' ? (
            <>
              <p className="text-sm text-gray-600">
                Počet importů: <span className="font-medium">{data.count}</span>
              </p>
              <p className="text-sm text-gray-600">
                Počet položek: <span className="font-medium">{data.itemCount}</span>
              </p>
            </>
          ) : (
            <>
              <p className="text-sm text-gray-600">
                Počet položek: <span className="font-medium">{data.itemCount}</span>
              </p>
              <p className="text-sm text-gray-600">
                Počet importů: <span className="font-medium">{data.count}</span>
              </p>
            </>
          )}
          {data.isWeekend && (
            <p className="text-xs text-blue-600">
              📅 Víkend
            </p>
          )}
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

  // Find weekend periods for highlighting
  const weekendPeriods = React.useMemo(() => {
    const periods: Array<{ start: string; end: string }> = [];
    let weekendStart: string | null = null;

    chartData.forEach((point, index) => {
      if (point.isWeekend && !weekendStart) {
        weekendStart = point.displayDate;
      } else if (!point.isWeekend && weekendStart) {
        periods.push({
          start: weekendStart,
          end: chartData[index - 1]?.displayDate || weekendStart,
        });
        weekendStart = null;
      }
    });

    // Handle weekend period that extends to the end
    if (weekendStart) {
      periods.push({
        start: weekendStart,
        end: chartData[chartData.length - 1]?.displayDate || weekendStart,
      });
    }

    return periods;
  }, [chartData]);

  return (
    <div className="w-full">
      {/* Chart title and info */}
      <div className="mb-4">
        <h3 className="text-lg font-medium text-gray-900 mb-1">
          Import banky - přehled posledních dní
        </h3>
        <p className="text-sm text-gray-600">
          Zobrazení podle: {dateType === 'ImportDate' ? 'datum importu bankovního výpisu' : 'datum bankovního výpisu'}
        </p>
        <p className="text-sm text-gray-600">
          Metrika: {viewType === 'ImportCount' ? 'počet importů' : 'počet položek výpisů'}
        </p>
        <p className="text-sm text-gray-600">
          Minimální prah: <span className="font-medium">{minimumThreshold} {viewType === 'ImportCount' ? 'importů' : 'položek'}/den</span>
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
              label={{ 
                value: viewType === 'ImportCount' ? 'Počet importů' : 'Počet položek', 
                angle: -90, 
                position: 'insideLeft' 
              }}
            />
            <Tooltip content={<CustomTooltip />} />
            
            {/* Weekend highlighting */}
            {weekendPeriods.map((period, index) => (
              <ReferenceArea
                key={index}
                x1={period.start}
                x2={period.end}
                fill="#e0f2fe"
                fillOpacity={0.3}
                strokeOpacity={0}
              />
            ))}
            
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
              dataKey={viewType === 'ImportCount' ? 'count' : 'itemCount'}
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
          <span className="text-gray-600">
            {viewType === 'ImportCount' ? 'Počet importů' : 'Počet položek'}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-4 h-0.5 bg-red-600 border-dashed border-t-2"></div>
          <span className="text-gray-600">Minimální prah</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-3 h-3 bg-red-600 rounded-full border-2 border-white"></div>
          <span className="text-gray-600">Problémové dny</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-4 h-3 bg-sky-100 border border-sky-200"></div>
          <span className="text-gray-600">Víkendy</span>
        </div>
      </div>
    </div>
  );
};