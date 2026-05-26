import React, { useState } from "react";
import { ChartOptions, type ChartData } from "chart.js";
import {
  TrendingUp,
  TrendingDown,
  DollarSign,
  AlertTriangle,
  Calendar,
  Package,
  BarChart3,
  ChevronDown,
  ChevronUp,
} from "lucide-react";
import { useFinancialOverviewQuery } from "../../api/hooks/useFinancialOverview";
import { useDepartments } from "../../api/hooks/useDepartments";
import { useIsMobile } from "../../hooks/useMediaQuery";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import {
  type PeriodType,
  formatCurrency,
  getPeriodLabel,
} from "./financial-overview/utils";
import { FinancialFilters } from "./financial-overview/FinancialFilters";
import { FinancialChart } from "./financial-overview/FinancialChart";
import { FinancialDataTable } from "./financial-overview/FinancialDataTable";
import { FinancialDataCards } from "./financial-overview/FinancialDataCards";
import { useScreenView } from '../../telemetry/useScreenView';

const FinancialOverview: React.FC = () => {
  const [selectedPeriod, setSelectedPeriod] =
    useState<PeriodType>("current-year");
  const [includeStockData, setIncludeStockData] = useState<boolean>(true);
  const [includeCurrentMonth, setIncludeCurrentMonth] = useState<boolean>(false);
  const [excludedDepartments, setExcludedDepartments] = useState<string[]>([]);
  const [isDataExpanded, setIsDataExpanded] = useState(false);
  const isMobile = useIsMobile();
  const initialDefaultsSet = React.useRef(false);

  useScreenView('Finance', 'FinancialOverview');

  const { data: departments } = useDepartments();

  React.useEffect(() => {
    if (departments && !initialDefaultsSet.current) {
      initialDefaultsSet.current = true;
      const buvol = departments.find((d) => d.name === "Buvol");
      if (buvol) {
        setExcludedDepartments([buvol.id]);
      }
    }
  }, [departments]);

  // Convert period type to months for API call.
  // For "current-year" periods, we count completed months only (now.getMonth() = 0-indexed).
  // The +1 for includeCurrentMonth is handled by the backend via the includeCurrentMonth flag,
  // but we still need to pass the correct total month count.
  const getMonthsFromPeriod = (period: PeriodType): number => {
    const now = new Date();
    const currentMonthOffset = includeCurrentMonth ? 1 : 0;
    switch (period) {
      case "current-year":
        return now.getMonth() + currentMonthOffset;
      case "current-and-previous-year":
        return now.getMonth() + currentMonthOffset + 12;
      case "last-6-months":
        return 6;
      case "last-13-months":
        return 13;
      case "last-26-months":
        return 26;
      default: {
        const _exhaustive: never = period;
        throw new Error(`Unhandled period: ${_exhaustive}`);
      }
    }
  };

  const months = getMonthsFromPeriod(selectedPeriod);
  const { data, isLoading, error, isRefetching } = useFinancialOverviewQuery(
    months,
    includeStockData,
    excludedDepartments,
    includeCurrentMonth,
  );

  const chartData = React.useMemo(() => {
    if (!data?.data) return null;

    const sortedData = [...data.data].sort((a, b) => {
      if (a.year !== b.year) return a.year - b.year;
      return a.month - b.month;
    });

    const labels = sortedData.map((item) => item.monthYearDisplay);
    const incomeData = sortedData.map((item) => item.income);
    const expensesData = sortedData.map((item) => item.expenses);
    const balanceData = sortedData.map((item) => item.financialBalance);
    const stockChangeData = sortedData.map(
      (item) => item.totalStockValueChange || 0,
    );
    const totalBalanceData = sortedData.map(
      (item) => item.totalBalance || item.financialBalance,
    );

    const datasets: any[] = [
      {
        label: "Příjmy",
        type: "bar" as const,
        data: incomeData,
        backgroundColor: "rgba(34, 197, 94, 0.6)",
        borderColor: "rgb(34, 197, 94)",
        borderWidth: 1,
      },
      {
        label: "Náklady",
        type: "bar" as const,
        data: expensesData,
        backgroundColor: "rgba(239, 68, 68, 0.6)",
        borderColor: "rgb(239, 68, 68)",
        borderWidth: 1,
      },
      {
        label: "Účetní bilance",
        type: "line" as const,
        data: balanceData,
        borderColor: "rgb(59, 130, 246)",
        backgroundColor: "rgba(59, 130, 246, 0.1)",
        fill: false,
        tension: 0.1,
        borderWidth: 3,
      },
    ];

    if (includeStockData) {
      datasets.push(
        {
          label: "Změna hodnoty skladu",
          type: "bar" as const,
          data: stockChangeData,
          backgroundColor: "rgba(168, 85, 247, 0.6)",
          borderColor: "rgb(168, 85, 247)",
          borderWidth: 1,
        },
        {
          label: "Celková bilance (vč. skladu)",
          type: "line" as const,
          data: totalBalanceData,
          borderColor: "rgb(245, 158, 11)",
          backgroundColor: "rgba(245, 158, 11, 0.1)",
          fill: false,
          tension: 0.1,
          borderWidth: 4,
        },
      );
    }

    return { labels, datasets } as ChartData<"bar">;
  }, [data?.data, includeStockData]);

  const chartOptions: ChartOptions<"bar"> = React.useMemo(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: isMobile ? ("top" as const) : ("right" as const),
          align: "center" as const,
          labels: {
            boxWidth: 12,
            padding: isMobile ? 5 : 8,
            font: { size: isMobile ? 10 : 11 },
          },
        },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: function (context) {
              return `${context.dataset.label}: ${formatCurrency(context.parsed.y ?? 0)}`;
            },
          },
        },
      },
      scales: {
        y: {
          beginAtZero: false,
          ticks: {
            callback: function (value) {
              return formatCurrency(Number(value));
            },
          },
          grid: {
            color: function (context) {
              if (context.tick.value === 0) return "#374151";
              return "#e5e7eb";
            },
            lineWidth: function (context) {
              if (context.tick.value === 0) return 3;
              return 1;
            },
          },
        },
      },
      interaction: { intersect: false, mode: "index" },
    }),
    [isMobile],
  );

  if (isLoading) {
    return (
      <div className="w-full max-w-none px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-center py-12">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
          <span className="ml-2 text-gray-600">Načítám finanční data...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="w-full max-w-none px-4 sm:px-6 lg:px-8">
        <div className="mb-8 p-4 bg-red-50 border border-red-200 rounded-lg">
          <div className="flex items-center">
            <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
            <h3 className="text-red-800 font-medium">
              Chyba při načítání finančních dat
            </h3>
          </div>
          <p className="mt-1 text-red-700 text-sm">
            {error.message || "Neznámá chyba"}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Finanční přehled</h1>
        <p className="mt-1 text-gray-600">
          Přehled příjmů, nákladů a celkové bilance firmy
        </p>
      </div>

      <div className="flex-1 overflow-auto">
        <FinancialFilters
          selectedPeriod={selectedPeriod}
          includeStockData={includeStockData}
          includeCurrentMonth={includeCurrentMonth}
          excludedDepartments={excludedDepartments}
          departments={departments}
          isRefetching={isRefetching}
          onPeriodChange={setSelectedPeriod}
          onIncludeStockDataChange={setIncludeStockData}
          onIncludeCurrentMonthChange={setIncludeCurrentMonth}
          onExcludedDepartmentsChange={setExcludedDepartments}
        />

        {/* Summary Cards */}
        {data?.summary && (
          <div
            className={`grid grid-cols-2 md:grid-cols-2 ${includeStockData ? "xl:grid-cols-6" : "lg:grid-cols-4"} gap-4 mb-6`}
          >
            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <TrendingUp className="h-5 w-5 text-emerald-400" />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Celkové příjmy
                      </dt>
                      <dd className="text-sm font-medium text-gray-900">
                        {formatCurrency(data.summary.totalIncome)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <TrendingDown className="h-5 w-5 text-red-400" />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Celkové náklady
                      </dt>
                      <dd className="text-sm font-medium text-gray-900">
                        {formatCurrency(data.summary.totalExpenses)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <DollarSign
                      className={`h-5 w-5 ${data.summary.totalBalance >= 0 ? "text-emerald-400" : "text-red-400"}`}
                    />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Účetní bilance
                      </dt>
                      <dd
                        className={`text-sm font-medium ${data.summary.totalBalance >= 0 ? "text-emerald-600" : "text-red-600"}`}
                      >
                        {formatCurrency(data.summary.totalBalance)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <Calendar
                      className={`h-5 w-5 ${data.summary.averageMonthlyBalance >= 0 ? "text-blue-400" : "text-red-400"}`}
                    />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Průměrná měsíční bilance
                      </dt>
                      <dd
                        className={`text-sm font-medium ${data.summary.averageMonthlyBalance >= 0 ? "text-blue-600" : "text-red-600"}`}
                      >
                        {formatCurrency(data.summary.averageMonthlyBalance)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            {includeStockData && data.summary.stockSummary && (
              <>
                <div className="bg-white overflow-hidden shadow rounded-lg">
                  <div className="p-3">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <Package
                          className={`h-5 w-5 ${data.summary.stockSummary.totalStockValueChange && data.summary.stockSummary.totalStockValueChange >= 0 ? "text-purple-400" : "text-orange-400"}`}
                        />
                      </div>
                      <div className="ml-3 w-0 flex-1">
                        <dl>
                          <dt className="text-xs font-medium text-gray-500 truncate">
                            Změna hodnoty skladu
                          </dt>
                          <dd
                            className={`text-sm font-medium ${data.summary.stockSummary.totalStockValueChange && data.summary.stockSummary.totalStockValueChange >= 0 ? "text-purple-600" : "text-orange-600"}`}
                          >
                            {formatCurrency(
                              data.summary.stockSummary.totalStockValueChange || 0,
                            )}
                          </dd>
                        </dl>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="bg-white overflow-hidden shadow rounded-lg">
                  <div className="p-3">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <BarChart3
                          className={`h-5 w-5 ${data.summary.stockSummary.totalBalanceWithStock && data.summary.stockSummary.totalBalanceWithStock >= 0 ? "text-emerald-400" : "text-red-400"}`}
                        />
                      </div>
                      <div className="ml-3 w-0 flex-1">
                        <dl>
                          <dt className="text-xs font-medium text-gray-500 truncate">
                            Celková bilance vč. skladu
                          </dt>
                          <dd
                            className={`text-sm font-medium ${data.summary.stockSummary.totalBalanceWithStock && data.summary.stockSummary.totalBalanceWithStock >= 0 ? "text-emerald-600" : "text-red-600"}`}
                          >
                            {formatCurrency(
                              data.summary.stockSummary.totalBalanceWithStock || 0,
                            )}
                          </dd>
                        </dl>
                      </div>
                    </div>
                  </div>
                </div>
              </>
            )}
          </div>
        )}

        {/* Chart */}
        {chartData && (
          <FinancialChart
            chartData={chartData}
            chartOptions={chartOptions}
            title={`Finanční přehled - ${getPeriodLabel(selectedPeriod)}${includeStockData ? " (včetně skladu)" : ""}`}
          />
        )}

        {/* Monthly data */}
        {data?.data && (
          <>
            {isMobile ? (
              <div className="mb-8">
                <button
                  type="button"
                  onClick={() => setIsDataExpanded((prev) => !prev)}
                  className="w-full flex items-center justify-between px-4 py-3 bg-white shadow sm:rounded-md text-left"
                >
                  <span className="text-base font-medium text-gray-900">
                    Měsíční data ({data.data.length})
                  </span>
                  {isDataExpanded ? (
                    <ChevronUp className="h-5 w-5 text-gray-500" />
                  ) : (
                    <ChevronDown className="h-5 w-5 text-gray-500" />
                  )}
                </button>
                {isDataExpanded && (
                  <div className="mt-2">
                    <FinancialDataCards
                      data={data.data}
                      includeStockData={includeStockData}
                    />
                  </div>
                )}
              </div>
            ) : (
              <div className="bg-white shadow sm:rounded-md mb-8">
                <div className="px-4 py-5 sm:px-6 border-b border-gray-200">
                  <h3 className="text-lg leading-6 font-medium text-gray-900">
                    Měsíční data
                  </h3>
                  <p className="mt-1 max-w-2xl text-sm text-gray-500">
                    Detailní rozpis příjmů, nákladů a bilance po jednotlivých
                    měsících{includeStockData ? " (včetně skladových dat)" : ""}
                  </p>
                </div>
                <FinancialDataTable
                  data={data.data}
                  includeStockData={includeStockData}
                />
              </div>
            )}
          </>
        )}

        {/* Empty state */}
        {data?.data && data.data.length === 0 && (
          <div className="text-center py-12">
            <DollarSign className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">
              Žádná finanční data
            </h3>
            <p className="mt-1 text-sm text-gray-500">
              Pro vybrané období nejsou k dispozici žádná finanční data.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

export default FinancialOverview;
