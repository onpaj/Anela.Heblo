import React from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from 'recharts';
import { format, parseISO } from 'date-fns';
import { cs } from 'date-fns/locale';
import { PackingMaterialLogDto, LogEntryType } from '../../../api/hooks/usePackingMaterials';

interface PackingMaterialConsumptionChartProps {
  data: PackingMaterialLogDto[];
}

interface ChartDataPoint {
  date: string;
  displayDate: string;
  newQuantity: number;
  changeAmount: number;
  logType: LogEntryType;
  logTypeText: string;
}

const PackingMaterialConsumptionChart: React.FC<PackingMaterialConsumptionChartProps> = ({ data }) => {
  // Transform data for chart - sort by date and create daily aggregated data
  const chartData: ChartDataPoint[] = React.useMemo(() => {
    if (!data || data.length === 0) return [];

    // Sort data by date
    const sortedData = [...data].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());

    return sortedData.map((item) => {
      const parsedDate = parseISO(item.date);
      
      return {
        date: item.date,
        displayDate: format(parsedDate, 'dd.MM.', { locale: cs }),
        newQuantity: item.newQuantity,
        changeAmount: item.changeAmount,
        logType: item.logType,
        logTypeText: item.logTypeText,
      };
    });
  }, [data]);

  // Custom tooltip component
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      const fullDate = format(parseISO(data.date), 'dd. MMMM yyyy', { locale: cs });
      
      return (
        <div className="bg-white p-3 border border-gray-200 rounded-lg shadow-lg">
          <p className="font-medium text-gray-900">{fullDate}</p>
          <p className="text-sm text-gray-600">
            Množství: <span className="font-medium">{data.newQuantity.toLocaleString('cs-CZ', {
              minimumFractionDigits: 0,
              maximumFractionDigits: 2
            })}</span>
          </p>
          <p className="text-sm text-gray-600">
            Změna: <span className={`font-medium ${data.changeAmount > 0 ? 'text-green-600' : 'text-red-600'}`}>
              {data.changeAmount > 0 ? '+' : ''}{data.changeAmount.toLocaleString('cs-CZ', {
                minimumFractionDigits: 0,
                maximumFractionDigits: 2
              })}
            </span>
          </p>
          <p className="text-xs text-gray-500">
            Typ: {data.logTypeText}
          </p>
        </div>
      );
    }
    return null;
  };

  // Custom dot component to differentiate log types
  const CustomDot = (props: any) => {
    const { cx, cy, payload } = props;
    const color = payload.logType === LogEntryType.Manual ? '#10b981' : '#f59e0b';
    
    return (
      <circle
        cx={cx}
        cy={cy}
        r={3}
        fill={color}
        stroke="#fff"
        strokeWidth={2}
      />
    );
  };

  if (!chartData || chartData.length === 0) {
    return (
      <div className="flex items-center justify-center h-64 text-gray-500">
        <div className="text-center">
          <p className="text-sm">Žádná data pro zobrazení</p>
          <p className="text-xs">Za posledních 60 dní nejsou k dispozici žádné záznamy změn</p>
        </div>
      </div>
    );
  }

  return (
    <div className="w-full">
      {/* Chart title */}
      <div className="mb-4">
        <h4 className="text-base font-medium text-gray-900 mb-1">
          Vývoj množství materiálu
        </h4>
        <p className="text-sm text-gray-600">
          Zobrazuje aktuální množství materiálu v čase na základě zaznamenaných změn
        </p>
      </div>

      {/* Chart container */}
      <div className="h-64 w-full">
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
                value: 'Množství', 
                angle: -90, 
                position: 'insideLeft',
                style: { textAnchor: 'middle' }
              }}
            />
            <Tooltip content={<CustomTooltip />} />
            
            {/* Main data line */}
            <Line
              type="monotone"
              dataKey="newQuantity"
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
          <span className="text-gray-600">Množství v čase</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-3 h-3 bg-emerald-500 rounded-full border-2 border-white"></div>
          <span className="text-gray-600">Ruční změna</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-3 h-3 bg-amber-500 rounded-full border-2 border-white"></div>
          <span className="text-gray-600">Automatická spotřeba</span>
        </div>
      </div>
    </div>
  );
};

export default PackingMaterialConsumptionChart;