import React, { useMemo, useState } from "react";
import { Chart } from "react-chartjs-2";
import { ChartOptions } from "chart.js";
import { Factory, TrendingUp, RefreshCw } from "lucide-react";
import {
  useManufactureOutputQuery,
  formatMonthDisplay,
  getMonthShortName,
  ManufactureOutputMonth,
} from "../../api/hooks/useManufactureOutput";
import ManufactureOutputModal from "./ManufactureOutputModal";

// Color palette for products (from highest weighted value to lowest)
const PRODUCT_COLORS = [
  "#1E40AF", // Deep blue
  "#3B82F6", // Blue
  "#60A5FA", // Light blue
  "#0891B2", // Cyan
  "#06B6D4", // Cyan-blue
  "#059669", // Emerald green
  "#10B981", // Green
  "#34D399", // Light green
  "#6366F1", // Indigo
  "#8B5CF6", // Purple
  "#A855F7", // Violet
  "#C084FC", // Light violet
  "#EC4899", // Pink
  "#F472B6", // Light pink
  "#F97316", // Orange
];

const OTHER_COLOR = "#9CA3AF"; // Gray for other products

const ManufactureOutput: React.FC = () => {
  const { data, isLoading, error, refetch } = useManufactureOutputQuery(13);
  const [selectedMonth, setSelectedMonth] =
    useState<ManufactureOutputMonth | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);

  const chartData = useMemo(() => {
    if (!data?.months) return null;

    const labels = data.months.map((m) => getMonthShortName(m.month));

    // Collect all unique products across all months and sort by total weighted value
    const productTotals = new Map<string, { name: string; total: number }>();

    data.months.forEach((month) => {
      month.products.forEach((product) => {
        const existing = productTotals.get(product.productCode) || {
          name: product.productName,
          total: 0,
        };
        productTotals.set(product.productCode, {
          name: product.productName,
          total: existing.total + product.weightedValue,
        });
      });
    });

    // Sort products by total weighted value and take top 15
    const sortedProducts = Array.from(productTotals.entries()).sort(
      (a, b) => b[1].total - a[1].total,
    );

    const topProducts = sortedProducts.slice(0, 15);
    const topProductCodes = new Set(topProducts.map((p) => p[0]));

    // Create color map for top products
    const productColorMap = new Map<string, string>();
    topProducts.forEach((product, index) => {
      productColorMap.set(
        product[0],
        PRODUCT_COLORS[index % PRODUCT_COLORS.length],
      );
    });

    // Create datasets
    const datasets: any[] = [];

    // Add "Other" category first (will be at bottom of stack)
    const hasOtherProducts = sortedProducts.length > 15;
    if (hasOtherProducts) {
      datasets.push({
        label: "Ostatní produkty",
        data: data.months.map((month) => {
          const otherValue = month.products
            .filter((p) => !topProductCodes.has(p.productCode))
            .reduce((sum, p) => sum + p.weightedValue, 0);
          return otherValue;
        }),
        backgroundColor: OTHER_COLOR,
        borderColor: OTHER_COLOR,
        borderWidth: 1,
      });
    }

    // Add datasets for top products (in reverse order so highest is on top)
    [...topProducts].reverse().forEach(([productCode, productInfo]) => {
      const color = productColorMap.get(productCode)!;

      datasets.push({
        label: productInfo.name,
        data: data.months.map((month) => {
          const product = month.products.find(
            (p) => p.productCode === productCode,
          );
          return product?.weightedValue || 0;
        }),
        backgroundColor: color,
        borderColor: color,
        borderWidth: 1,
      });
    });

    return {
      labels,
      datasets,
    };
  }, [data]);

  const chartOptions: ChartOptions<"bar"> = {
    responsive: true,
    maintainAspectRatio: false,
    onClick: (event: any, elements: any[]) => {
      if (elements.length > 0 && data?.months) {
        const monthIndex = elements[0].index;
        const monthData = data.months[monthIndex];
        setSelectedMonth(monthData);
        setIsModalOpen(true);
      }
    },
    plugins: {
      legend: {
        position: "bottom" as const,
        labels: {
          boxWidth: 15,
          padding: 10,
          font: {
            size: 11,
          },
        },
      },
      title: {
        display: false,
      },
      tooltip: {
        callbacks: {
          label: (context: any) => {
            const value = context.parsed.y;
            return `${context.dataset.label}: ${value.toFixed(1)}`;
          },
          afterLabel: (context: any) => {
            if (!data?.months) return "";

            const monthData = data.months[context.dataIndex];
            const datasetLabel = context.dataset.label;

            if (datasetLabel === "Ostatní produkty") {
              return "";
            }

            const product = monthData.products.find(
              (p) => p.productName === datasetLabel,
            );
            if (product) {
              return [
                `Množství: ${product.quantity.toFixed(1)}`,
                `Náročnost: ${product.difficulty.toFixed(1)}`,
              ];
            }
            return "";
          },
        },
      },
    },
    scales: {
      x: {
        stacked: true,
        grid: {
          display: false,
        },
      },
      y: {
        stacked: true,
        title: {
          display: true,
          text: "Vážený výtlak výroby",
        },
        beginAtZero: true,
      },
    },
  };

  // Calculate summary statistics
  const summaryStats = useMemo(() => {
    if (!data?.months) return null;

    const totalOutput = data.months.reduce(
      (sum, month) => sum + month.totalOutput,
      0,
    );
    const avgMonthlyOutput = totalOutput / data.months.length;

    // Find month with highest output
    const maxMonth = data.months.reduce(
      (max, month) => (month.totalOutput > max.totalOutput ? month : max),
      data.months[0],
    );

    // Find most productive product overall
    const productTotals = new Map<string, { name: string; total: number }>();
    data.months.forEach((month) => {
      month.products.forEach((product) => {
        const existing = productTotals.get(product.productCode) || {
          name: product.productName,
          total: 0,
        };
        productTotals.set(product.productCode, {
          name: product.productName,
          total: existing.total + product.weightedValue,
        });
      });
    });

    const topProduct = Array.from(productTotals.entries()).sort(
      (a, b) => b[1].total - a[1].total,
    )[0];

    return {
      totalOutput,
      avgMonthlyOutput,
      maxMonth: maxMonth ? formatMonthDisplay(maxMonth.month) : "",
      maxMonthValue: maxMonth?.totalOutput || 0,
      topProduct: topProduct?.[1].name || "",
      topProductValue: topProduct?.[1].total || 0,
    };
  }, [data]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="text-red-600">
          Chyba při načítání dat: {(error as Error).message}
        </div>
      </div>
    );
  }

  return (
    <div className="absolute inset-0 flex flex-col bg-gray-50">
      {/* Header */}
      <div className="bg-white border-b border-gray-200 px-6 py-4 flex-shrink-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center space-x-3">
            <Factory className="h-6 w-6 text-indigo-600" />
            <h1 className="text-2xl font-semibold text-gray-900">
              Přehled výroby
            </h1>
          </div>
          <button
            onClick={() => refetch()}
            className="flex items-center px-4 py-2 bg-white border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            <RefreshCw className="h-4 w-4 mr-2" />
            Obnovit
          </button>
        </div>
      </div>

      {/* Summary Stats */}
      {summaryStats && (
        <div className="bg-white border-b border-gray-200 px-6 py-3 flex-shrink-0">
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div>
              <p className="text-sm text-gray-600">Celkový výtlak</p>
              <p className="text-lg font-semibold text-gray-900">
                {summaryStats.totalOutput.toFixed(1)}
              </p>
            </div>
            <div>
              <p className="text-sm text-gray-600">Průměr za měsíc</p>
              <p className="text-lg font-semibold text-gray-900">
                {summaryStats.avgMonthlyOutput.toFixed(1)}
              </p>
            </div>
            <div>
              <p className="text-sm text-gray-600">Nejproduktivnější měsíc</p>
              <p className="text-lg font-semibold text-gray-900">
                {summaryStats.maxMonth}
              </p>
              <p className="text-sm text-gray-500">
                ({summaryStats.maxMonthValue.toFixed(1)})
              </p>
            </div>
            <div>
              <p className="text-sm text-gray-600">Nejproduktivnější produkt</p>
              <p className="text-lg font-semibold text-gray-900 truncate">
                {summaryStats.topProduct}
              </p>
              <p className="text-sm text-gray-500">
                ({summaryStats.topProductValue.toFixed(1)})
              </p>
            </div>
          </div>
        </div>
      )}

      {/* Chart - takes full remaining height */}
      <div className="flex-1 p-4 min-h-0">
        <div className="bg-white rounded-lg shadow p-4 h-full flex flex-col">
          <div className="flex items-center justify-between mb-3 flex-shrink-0">
            <h2 className="text-lg font-medium text-gray-900">
              Vážený výtlak výroby za posledních 13 měsíců
            </h2>
            <TrendingUp className="h-5 w-5 text-gray-400" />
          </div>

          {chartData && (
            <div
              className="flex-1 min-h-0"
              style={{ height: "calc(100% - 40px)" }}
            >
              <Chart type="bar" data={chartData} options={chartOptions} />
            </div>
          )}
        </div>
      </div>

      {/* Modal */}
      <ManufactureOutputModal
        isOpen={isModalOpen}
        onClose={() => {
          setIsModalOpen(false);
          setSelectedMonth(null);
        }}
        monthData={selectedMonth}
      />
    </div>
  );
};

export default ManufactureOutput;
