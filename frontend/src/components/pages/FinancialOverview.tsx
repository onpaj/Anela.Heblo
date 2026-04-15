import React, { useState } from "react";
import { Chart } from "react-chartjs-2";
import { ChartOptions } from "chart.js";
import {
  TrendingUp,
  TrendingDown,
  DollarSign,
  AlertTriangle,
  Calendar,
  Package,
  BarChart3,
} from "lucide-react";
import { useFinancialOverviewQuery } from "../../api/hooks/useFinancialOverview";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

type PeriodType =
  | "current-year"
  | "current-and-previous-year"
  | "last-6-months"
  | "last-13-months"
  | "last-26-months";

const FinancialOverview: React.FC = () => {
  const [selectedPeriod, setSelectedPeriod] =
    useState<PeriodType>("current-year");
  const [includeStockData, setIncludeStockData] = useState<boolean>(true);
  const [windowWidth, setWindowWidth] = useState(window.innerWidth);

  React.useEffect(() => {
    const handleResize = () => setWindowWidth(window.innerWidth);
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, []);

  // Convert period type to months for API call
  const getMonthsFromPeriod = (period: PeriodType): number => {
    const now = new Date();
    switch (period) {
      case "current-year":
        return now.getMonth() + 1; // Months from January to current month
      case "current-and-previous-year":
        return now.getMonth() + 1 + 12; // Current year months + 12 previous months
      case "last-6-months":
        return 6;
      case "last-13-months":
        return 13;
      case "last-26-months":
        return 26;
      default:
        return 6;
    }
  };

  const months = getMonthsFromPeriod(selectedPeriod);
  const { data, isLoading, error, isRefetching } = useFinancialOverviewQuery(
    months,
    includeStockData,
  );

  const formatCurrency = (amount: number) => {
    return new Intl.NumberFormat("cs-CZ", {
      style: "currency",
      currency: "CZK",
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(amount);
  };

  // Prepare chart data
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
        backgroundColor: "rgba(34, 197, 94, 0.6)", // Zelená pro příjmy
        borderColor: "rgb(34, 197, 94)",
        borderWidth: 1,
      },
      {
        label: "Náklady",
        type: "bar" as const,
        data: expensesData,
        backgroundColor: "rgba(239, 68, 68, 0.6)", // Červená pro náklady
        borderColor: "rgb(239, 68, 68)",
        borderWidth: 1,
      },
      {
        label: "Účetní bilance",
        type: "line" as const,
        data: balanceData,
        borderColor: "rgb(59, 130, 246)", // Modrá pro finanční bilanci
        backgroundColor: "rgba(59, 130, 246, 0.1)",
        fill: false,
        tension: 0.1,
        borderWidth: 3,
      },
    ];

    // Add stock data series if included
    if (includeStockData) {
      datasets.push(
        {
          label: "Změna hodnoty skladu",
          type: "bar" as const,
          data: stockChangeData,
          backgroundColor: "rgba(168, 85, 247, 0.6)", // Fialová pro skladové změny
          borderColor: "rgb(168, 85, 247)",
          borderWidth: 1,
        },
        {
          label: "Celková bilance (vč. skladu)",
          type: "line" as const,
          data: totalBalanceData,
          borderColor: "rgb(245, 158, 11)", // Oranžová pro celkovou bilanci
          backgroundColor: "rgba(245, 158, 11, 0.1)",
          fill: false,
          tension: 0.1,
          borderWidth: 4, // Silnější linka pro zvýraznění
        },
      );
    }

    return {
      labels,
      datasets,
    };
  }, [data?.data, includeStockData]);

  const chartOptions: ChartOptions<"bar"> = React.useMemo(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: windowWidth < 768 ? ("top" as const) : ("right" as const),
          align: "center" as const,
          labels: {
            boxWidth: 12,
            padding: windowWidth < 768 ? 5 : 8,
            font: {
              size: windowWidth < 768 ? 10 : 11,
            },
          },
        },
        title: {
          display: false,
        },
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
              // Make the zero line bold and darker
              if (context.tick.value === 0) {
                return "#374151"; // Dark gray for zero line
              }
              return "#e5e7eb"; // Light gray for other grid lines
            },
            lineWidth: function (context) {
              // Make the zero line thicker
              if (context.tick.value === 0) {
                return 3;
              }
              return 1;
            },
          },
        },
      },
      interaction: {
        intersect: false,
        mode: "index",
      },
    }),
    [windowWidth],
  );

  const getPeriodLabel = (period: PeriodType): string => {
    switch (period) {
      case "current-year":
        return "Aktuální rok";
      case "current-and-previous-year":
        return "Aktuální + předchozí rok";
      case "last-6-months":
        return "Posledních 6 měsíců";
      case "last-13-months":
        return "Posledních 13 měsíců";
      case "last-26-months":
        return "Posledních 26 měsíců";
      default:
        return "Posledních 6 měsíců";
    }
  };

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
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">
          Finanční přehled
        </h1>
        <p className="mt-1 text-gray-600">
          Přehled příjmů, nákladů a celkové bilance firmy
        </p>
      </div>

      {/* Content Area */}
      <div className="flex-1 overflow-auto">
        {/* Controls */}
        <div className="mb-6 flex flex-col lg:flex-row lg:items-end lg:justify-between gap-4">
          <div className="flex flex-col sm:flex-row gap-4">
            <div>
              <label
                htmlFor="period-select"
                className="block text-sm font-medium text-gray-700 mb-2"
              >
                Časové období:
              </label>
              <select
                id="period-select"
                value={selectedPeriod}
                onChange={(e) =>
                  setSelectedPeriod(e.target.value as PeriodType)
                }
                className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="current-year">Aktuální rok</option>
                <option value="current-and-previous-year">
                  Aktuální + předchozí rok
                </option>
                <option value="last-6-months">Posledních 6 měsíců</option>
                <option value="last-13-months">Posledních 13 měsíců</option>
                <option value="last-26-months">Posledních 26 měsíců</option>
              </select>
            </div>

            <div>
              <label
                htmlFor="stock-toggle"
                className="block text-sm font-medium text-gray-700 mb-2"
              >
                Zobrazení dat:
              </label>
              <div className="flex items-center">
                <input
                  id="stock-toggle"
                  type="checkbox"
                  checked={includeStockData}
                  onChange={(e) => setIncludeStockData(e.target.checked)}
                  className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
                />
                <label
                  htmlFor="stock-toggle"
                  className="ml-2 block text-sm text-gray-900 flex items-center"
                >
                  <Package className="w-4 h-4 mr-1" />
                  Zahrnout skladová data
                </label>
              </div>
            </div>
          </div>

          {isRefetching && (
            <div className="flex items-center text-sm text-gray-500">
              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-indigo-600 mr-2"></div>
              Aktualizuji data...
            </div>
          )}
        </div>

        {/* Summary Cards */}
        {data?.summary && (
          <div
            className={`grid grid-cols-1 sm:grid-cols-2 ${includeStockData ? "xl:grid-cols-6" : "lg:grid-cols-4"} gap-4 mb-6`}
          >
            {/* Total Income */}
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

            {/* Total Expenses */}
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

            {/* Total Balance */}
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

            {/* Average Monthly Balance */}
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

            {/* Stock Summary Cards - shown only when stock data is included */}
            {includeStockData && data.summary.stockSummary && (
              <>
                {/* Total Stock Value Change */}
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
                              data.summary.stockSummary.totalStockValueChange ||
                                0,
                            )}
                          </dd>
                        </dl>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Total Balance with Stock */}
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
                              data.summary.stockSummary.totalBalanceWithStock ||
                                0,
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
          <div className="bg-white shadow rounded-lg mb-8">
            <div className="px-4 sm:px-6 pt-6 pb-2">
              <h3 className="text-lg font-medium text-gray-900">
                {`Finanční přehled - ${getPeriodLabel(selectedPeriod)}${includeStockData ? " (včetně skladu)" : ""}`}
              </h3>
            </div>
            <div className="relative w-full px-2 sm:px-4 lg:px-6 pb-6">
              <div className="h-[350px] sm:h-[400px] lg:h-[450px]">
                <Chart type="bar" data={chartData} options={chartOptions} />
              </div>
            </div>
          </div>
        )}

        {/* Data Table */}
        {data?.data && (
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
            <div className="overflow-auto" style={{ maxHeight: "400px" }}>
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50 sticky top-0 z-10">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Měsíc
                    </th>
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Příjmy
                    </th>
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Náklady
                    </th>
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Účetní bilance
                    </th>
                    {includeStockData && (
                      <>
                        <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                          Změna skladu
                        </th>
                        <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                          Celková bilance
                        </th>
                      </>
                    )}
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {data.data.map((item, index) => (
                    <tr
                      key={`${item.year}-${item.month}`}
                      className="hover:bg-gray-50"
                    >
                      <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                        {item.monthYearDisplay}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                        {formatCurrency(item.income)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                        {formatCurrency(item.expenses)}
                      </td>
                      <td
                        className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                          item.financialBalance >= 0
                            ? "text-emerald-600"
                            : "text-red-600"
                        }`}
                      >
                        {formatCurrency(item.financialBalance)}
                      </td>
                      {includeStockData && (
                        <>
                          <td
                            className={`px-6 py-4 whitespace-nowrap text-sm text-right ${
                              (item.totalStockValueChange || 0) >= 0
                                ? "text-purple-600"
                                : "text-orange-600"
                            }`}
                          >
                            {formatCurrency(item.totalStockValueChange || 0)}
                          </td>
                          <td
                            className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                              (item.totalBalance || item.financialBalance) >= 0
                                ? "text-emerald-600"
                                : "text-red-600"
                            }`}
                          >
                            {formatCurrency(
                              item.totalBalance || item.financialBalance,
                            )}
                          </td>
                        </>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Empty State */}
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
