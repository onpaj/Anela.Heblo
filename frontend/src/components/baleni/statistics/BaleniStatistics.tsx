import React from "react";
import { AlertCircle, BarChart3, RefreshCw } from "lucide-react";
import { format, parseISO, subDays } from "date-fns";
import { cs } from "date-fns/locale";
import { useScreenView } from "../../../telemetry/useScreenView";
import {
  PackingStatisticsResponse,
  usePackingStatistics,
} from "../../../api/hooks/usePackingStatistics";
import PackingHourHeatmap from "./PackingHourHeatmap";
import {
  CarrierMixChart,
  PackagesPerOrderChart,
  PackerLeaderboard,
  ThroughputChart,
} from "./PackingCharts";

const RANGE_PRESETS = [
  { id: "7", label: "7 dní", days: 7 },
  { id: "30", label: "30 dní", days: 30 },
  { id: "90", label: "90 dní", days: 90 },
] as const;

const DEFAULT_RANGE_DAYS = 30;
const ISO_DATE = "yyyy-MM-dd";

const Panel: React.FC<{ title: string; children: React.ReactNode; subtitle?: React.ReactNode }> = ({
  title,
  subtitle,
  children,
}) => (
  <div className="bg-white border border-border-light rounded-xl p-6 shadow-soft">
    <div className="mb-4">
      <h3 className="text-sm font-semibold text-neutral-slate">{title}</h3>
      {subtitle && <p className="text-xs text-neutral-gray mt-1">{subtitle}</p>}
    </div>
    {children}
  </div>
);

const KpiCard: React.FC<{ label: string; value: React.ReactNode; loading: boolean }> = ({
  label,
  value,
  loading,
}) => (
  <div className="bg-white border border-border-light rounded-xl p-5 shadow-soft">
    <p className="text-sm text-neutral-gray mb-2">{label}</p>
    {loading ? (
      <div className="h-8 w-20 bg-secondary-blue-pale rounded animate-pulse" />
    ) : (
      <p className="text-3xl font-bold text-primary-blue">{value}</p>
    )}
  </div>
);

const formatDay = (iso: string): string => format(parseISO(iso), "d. M. yyyy", { locale: cs });

const packerAttributionHint = (data: PackingStatisticsResponse): string | null => {
  if (!data.packerAttributionSince) return null;
  if (parseISO(data.packerAttributionSince) <= parseISO(data.fromDate)) return null;
  return `Evidence baličů je dostupná až od ${formatDay(data.packerAttributionSince)}.`;
};

const BaleniStatistics: React.FC = () => {
  useScreenView("Baleni", "Statistics");
  const [rangeDays, setRangeDays] = React.useState<number>(DEFAULT_RANGE_DAYS);

  const params = React.useMemo(() => {
    const today = new Date();
    return {
      fromDate: format(subDays(today, rangeDays - 1), ISO_DATE),
      toDate: format(today, ISO_DATE),
    };
  }, [rangeDays]);

  const { data, isLoading, error, refetch, isFetching } = usePackingStatistics(params);

  if (error) {
    return (
      <div className="py-6">
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <div className="flex items-center gap-3">
            <AlertCircle className="h-5 w-5 text-red-600" />
            <div>
              <h3 className="text-sm font-medium text-red-800">Chyba při načítání statistik</h3>
              <p className="text-sm text-red-700 mt-1">
                {error instanceof Error ? error.message : "Neočekávaná chyba"}
              </p>
            </div>
          </div>
          <button
            onClick={() => refetch()}
            className="mt-3 px-3 py-1 text-sm bg-red-100 text-red-800 rounded hover:bg-red-200 transition-colors"
          >
            Zkusit znovu
          </button>
        </div>
      </div>
    );
  }

  const summary = data?.summary;
  const attributionHint = data ? packerAttributionHint(data) : null;

  return (
    <div className="pt-4 space-y-6" data-testid="baleni-statistics">
      {/* Header */}
      <div className="flex items-center justify-between flex-wrap gap-3">
        <div>
          <h1 className="text-2xl font-bold text-neutral-slate flex items-center gap-3">
            <BarChart3 className="h-6 w-6 text-primary-blue" />
            Statistiky balicí stanice
          </h1>
          {data && (
            <p className="text-sm text-neutral-gray mt-1">
              {formatDay(data.fromDate)} – {formatDay(data.toDate)}
            </p>
          )}
        </div>

        <div className="flex items-center gap-2">
          {RANGE_PRESETS.map((preset) => (
            <button
              key={preset.id}
              onClick={() => setRangeDays(preset.days)}
              className={`px-3 py-2 rounded-lg border text-sm transition-colors ${
                rangeDays === preset.days
                  ? "bg-secondary-blue-pale border-primary-blue text-primary-blue"
                  : "bg-white border-border-light text-neutral-gray hover:bg-secondary-blue-pale"
              }`}
            >
              {preset.label}
            </button>
          ))}
          <button
            onClick={() => refetch()}
            disabled={isFetching}
            className="flex items-center gap-2 px-3 py-2 rounded-lg border border-border-light text-neutral-gray hover:bg-secondary-blue-pale transition-colors disabled:opacity-50"
            aria-label="Obnovit"
          >
            <RefreshCw className={`h-4 w-4 ${isFetching ? "animate-spin" : ""}`} />
          </button>
        </div>
      </div>

      {/* KPI cards */}
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-4">
        <KpiCard label="Balíků" value={summary?.totalPackages ?? "—"} loading={isLoading} />
        <KpiCard label="Objednávek" value={summary?.totalOrders ?? "—"} loading={isLoading} />
        <KpiCard
          label="Ø balíků / obj."
          value={summary ? summary.averagePackagesPerOrder.toFixed(2) : "—"}
          loading={isLoading}
        />
        <KpiCard
          label="Pokrytí trackingu"
          value={summary ? `${summary.trackingCoveragePercent} %` : "—"}
          loading={isLoading}
        />
        <KpiCard label="Baličů" value={summary?.distinctPackers ?? "—"} loading={isLoading} />
        <KpiCard
          label="Nejvytíženější den"
          value={
            summary?.busiestDay
              ? format(parseISO(summary.busiestDay.date), "d. M.", { locale: cs })
              : "—"
          }
          loading={isLoading}
        />
      </div>

      {isLoading || !data ? (
        <div className="flex items-center justify-center h-72 bg-white border border-border-light rounded-xl shadow-soft">
          <div className="text-center">
            <RefreshCw className="h-8 w-8 text-primary-blue animate-spin mx-auto mb-4" />
            <p className="text-neutral-gray">Načítání dat...</p>
          </div>
        </div>
      ) : (
        <>
          <Panel title="Průběh balení v čase" subtitle="Počet zabalených balíků a objednávek po dnech">
            <ThroughputChart data={data.throughputDaily} />
          </Panel>

          <Panel
            title="Vytížení podle hodin"
            subtitle="Počet zabalených balíků podle dne v týdnu a hodiny (místní čas)"
          >
            <PackingHourHeatmap data={data.hourHeatmap} />
          </Panel>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <Panel title="Baliči" subtitle={attributionHint ?? "Počet zabalených objednávek na baliče"}>
              <PackerLeaderboard data={data.byPacker} />
            </Panel>

            <Panel title="Dopravci" subtitle="Podíl balíků podle dopravce">
              <CarrierMixChart data={data.byCarrier} />
            </Panel>
          </div>

          <Panel title="Balíků na objednávku" subtitle="Rozložení objednávek podle počtu balíků">
            <PackagesPerOrderChart data={data.packagesPerOrder} />
          </Panel>
        </>
      )}
    </div>
  );
};

export default BaleniStatistics;
