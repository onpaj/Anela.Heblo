import React from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { format, parseISO } from "date-fns";
import { cs } from "date-fns/locale";
import {
  CarrierMix,
  DailyThroughput,
  PackagesPerOrderBucket,
  PackerThroughput,
} from "../../../api/hooks/usePackingStatistics";

const CARRIER_COLORS = ["#2563eb", "#0ea5e9", "#14b8a6", "#f59e0b", "#a855f7", "#ec4899", "#64748b"];
const EmptyState: React.FC = () => (
  <p className="text-sm text-neutral-gray italic">Žádná data k zobrazení.</p>
);

export const ThroughputChart: React.FC<{ data: DailyThroughput[] }> = ({ data }) => {
  if (data.length === 0) return <EmptyState />;
  const chartData = data.map((d) => ({
    ...d,
    label: format(parseISO(d.date), "dd.MM.", { locale: cs }),
  }));
  return (
    <div className="h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 10 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis dataKey="label" tick={{ fontSize: 11 }} stroke="#6b7280" />
          <YAxis tick={{ fontSize: 11 }} stroke="#6b7280" allowDecimals={false} />
          <Tooltip
            formatter={(value, name) => [
              value,
              name === "packageCount" ? "Balíků" : "Objednávek",
            ]}
            labelFormatter={(label) => `Den ${label}`}
          />
          <Legend
            formatter={(value) => (value === "packageCount" ? "Balíků" : "Objednávek")}
          />
          <Bar dataKey="packageCount" fill="#2563eb" radius={[2, 2, 0, 0]} />
          <Bar dataKey="orderCount" fill="#93c5fd" radius={[2, 2, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
};

export const CarrierMixChart: React.FC<{ data: CarrierMix[] }> = ({ data }) => {
  if (data.length === 0) return <EmptyState />;
  return (
    <div className="h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie
            data={data}
            dataKey="packageCount"
            nameKey="name"
            cx="50%"
            cy="50%"
            innerRadius={55}
            outerRadius={90}
            paddingAngle={2}
          >
            {data.map((entry, index) => (
              <Cell key={entry.code} fill={CARRIER_COLORS[index % CARRIER_COLORS.length]} />
            ))}
          </Pie>
          <Tooltip formatter={(value) => [value, "Balíků"]} />
          <Legend />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
};

export const PackerLeaderboard: React.FC<{ data: PackerThroughput[] }> = ({ data }) => {
  if (data.length === 0) return <EmptyState />;
  return (
    <div className="w-full" style={{ height: Math.max(160, data.length * 44) }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          layout="vertical"
          data={data}
          margin={{ top: 5, right: 20, left: 10, bottom: 5 }}
        >
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" horizontal={false} />
          <XAxis type="number" tick={{ fontSize: 11 }} stroke="#6b7280" allowDecimals={false} />
          <YAxis
            type="category"
            dataKey="packerName"
            width={120}
            tick={{ fontSize: 12 }}
            stroke="#6b7280"
          />
          <Tooltip formatter={(value) => [value, "Objednávek"]} />
          <Bar dataKey="orderCount" fill="#2563eb" radius={[0, 4, 4, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
};

export const PackagesPerOrderChart: React.FC<{ data: PackagesPerOrderBucket[] }> = ({ data }) => {
  if (data.length === 0) return <EmptyState />;
  const chartData = data.map((b) => ({
    ...b,
    label: b.packageCount >= 3 ? "3+" : String(b.packageCount),
  }));
  return (
    <div className="h-60 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={chartData} margin={{ top: 10, right: 20, left: 0, bottom: 10 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
          <XAxis
            dataKey="label"
            tick={{ fontSize: 12 }}
            stroke="#6b7280"
            label={{ value: "Balíků v objednávce", position: "insideBottom", offset: -5, fontSize: 11 }}
          />
          <YAxis tick={{ fontSize: 11 }} stroke="#6b7280" allowDecimals={false} />
          <Tooltip formatter={(value) => [value, "Objednávek"]} />
          <Bar dataKey="orderCount" fill="#0ea5e9" radius={[2, 2, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
};
