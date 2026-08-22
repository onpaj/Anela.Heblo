import React from "react";
import { HourBucket } from "../../../api/hooks/usePackingStatistics";
import { useTheme } from "../../../contexts/ThemeContext";
import { GRAPHITE } from "../../common/reactSelectDarkStyles";

interface PackingHourHeatmapProps {
  data: HourBucket[];
}

const WEEKDAY_LABELS = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"]; // ISO order: Mon..Sun
const DEFAULT_FROM_HOUR = 6;
const DEFAULT_TO_HOUR = 20;

const cellKey = (dayOfWeek: number, hour: number): string => `${dayOfWeek}-${hour}`;

/**
 * Local weekday × hour heatmap of package counts. recharts has no native heatmap,
 * so this is a simple CSS grid with opacity scaled to each cell's share of the max.
 */
const PackingHourHeatmap: React.FC<PackingHourHeatmapProps> = ({ data }) => {
  const { theme } = useTheme();
  const isDark = theme === "dark";
  const counts = React.useMemo(() => {
    const map = new Map<string, number>();
    for (const bucket of data) {
      map.set(cellKey(bucket.dayOfWeek, bucket.hour), bucket.packageCount);
    }
    return map;
  }, [data]);

  const maxCount = React.useMemo(
    () => data.reduce((max, b) => Math.max(max, b.packageCount), 0),
    [data],
  );

  // Render the hour span that actually contains activity, falling back to working hours.
  const { fromHour, toHour } = React.useMemo(() => {
    if (data.length === 0) {
      return { fromHour: DEFAULT_FROM_HOUR, toHour: DEFAULT_TO_HOUR };
    }
    const hours = data.map((b) => b.hour);
    return { fromHour: Math.min(...hours), toHour: Math.max(...hours) };
  }, [data]);

  const hours = React.useMemo(() => {
    const result: number[] = [];
    for (let h = fromHour; h <= toHour; h++) result.push(h);
    return result;
  }, [fromHour, toHour]);

  if (data.length === 0) {
    return (
      <p className="text-sm text-neutral-gray italic dark:text-graphite-muted">Žádná data k zobrazení.</p>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="border-separate border-spacing-1" data-testid="packing-hour-heatmap">
        <thead>
          <tr>
            <th className="w-8" />
            {hours.map((hour) => (
              <th key={hour} className="text-xs font-normal text-neutral-gray text-center w-7 dark:text-graphite-muted">
                {hour}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {WEEKDAY_LABELS.map((label, index) => {
            const dayOfWeek = index + 1; // ISO 1..7
            return (
              <tr key={dayOfWeek}>
                <td className="text-xs text-neutral-gray pr-1 text-right dark:text-graphite-muted">{label}</td>
                {hours.map((hour) => {
                  const count = counts.get(cellKey(dayOfWeek, hour)) ?? 0;
                  const intensity = maxCount > 0 ? count / maxCount : 0;
                  return (
                    <td
                      key={hour}
                      className="h-7 w-7 rounded"
                      title={`${label} ${hour}:00 — ${count} balíků`}
                      style={{
                        backgroundColor:
                          count === 0
                            ? isDark
                              ? GRAPHITE.surface2
                              : "#f1f5f9"
                            : isDark
                              ? `rgba(56, 189, 248, ${0.35 + intensity * 0.65})`
                              : `rgba(37, 99, 235, ${0.15 + intensity * 0.85})`,
                      }}
                    />
                  );
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};

export default PackingHourHeatmap;
