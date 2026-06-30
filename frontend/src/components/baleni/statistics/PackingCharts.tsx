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

export const MAX_CARRIERS = 6;
export const OTHER_LABEL = "Ostatní";
export const OTHER_KEY = "__ostalni_bucket__"; // sentinel React key; never equals a real carrier name
const OTHER_COLOR = "#64748b"; // neutral slate for the "Ostatní" bucket

export interface CarrierSlice {
  /** Stable key for the React Cell + legend payload (slices are unique by name). */
  key: string;
  name: string;
  packageCount: number;
}

/**
 * Collapses raw CarrierMix rows into a bounded set of pie slices:
 * 1. merge entries sharing the same display `name` (sum packageCount),
 * 2. sort descending by packageCount,
 * 3. keep the top MAX_CARRIERS, rolling any remainder into a single
 *    "Ostatní" bucket (added only when >= 1 carrier remains).
 * Pure: does not mutate the input array or its elements.
 */
export function buildCarrierSlices(data: readonly CarrierMix[]): CarrierSlice[] {
  const mergedByName = new Map<string, CarrierSlice>();
  for (const { name, packageCount } of data) {
    const existing = mergedByName.get(name);
    if (existing) {
      mergedByName.set(name, {
        ...existing,
        packageCount: existing.packageCount + packageCount,
      });
    } else {
      mergedByName.set(name, { key: name, name, packageCount });
    }
  }

  const sorted = Array.from(mergedByName.values()).sort(
    (a, b) => b.packageCount - a.packageCount,
  );

  const top = sorted.slice(0, MAX_CARRIERS);
  const rest = sorted.slice(MAX_CARRIERS);

  if (rest.length === 0) {
    return top;
  }

  const otherTotal = rest.reduce((sum, s) => sum + s.packageCount, 0);
  return [...top, { key: OTHER_KEY, name: OTHER_LABEL, packageCount: otherTotal }];
}

function sliceColor(slice: CarrierSlice, index: number): string {
  if (slice.key === OTHER_KEY) return OTHER_COLOR;
  return CARRIER_COLORS[index % CARRIER_COLORS.length];
}

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
  const slices = buildCarrierSlices(data);
  return (
    <div className="h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie
            data={slices}
            dataKey="packageCount"
            nameKey="name"
            cx="50%"
            cy="45%"
            innerRadius={45}
            outerRadius={75}
            paddingAngle={2}
          >
            {slices.map((entry, index) => (
              <Cell key={entry.key} fill={sliceColor(entry, index)} />
            ))}
          </Pie>
          <Tooltip formatter={(value) => [value, "Balíků"]} />
          <Legend
            layout="horizontal"
            verticalAlign="bottom"
            align="center"
            iconSize={8}
            wrapperStyle={{
              fontSize: 11,
              lineHeight: "16px",
              maxHeight: 64,
              overflowY: "hidden",
              paddingTop: 4,
            }}
          />
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
