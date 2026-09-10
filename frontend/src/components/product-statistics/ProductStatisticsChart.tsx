import React from "react";
import { BarChart3 } from "lucide-react";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
} from "chart.js";
import { Line } from "react-chartjs-2";
import { getSeriesColor } from "./productStatisticsColors";

// Idempotent: CatalogDetail registers the same elements, but this page can render
// without CatalogDetail ever being mounted.
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
);

export interface ProductStatisticsSeries {
  productCode: string;
  productName: string;
  values: number[];
}

export interface ProductStatisticsChartProps {
  months: string[];
  series: ProductStatisticsSeries[];
  yAxisLabel: string;
  /** Palette slot per product code. Falls back to series order when a code is absent. */
  colorIndexByProduct?: ReadonlyMap<string, number>;
}

const ProductStatisticsChart: React.FC<ProductStatisticsChartProps> = ({
  months,
  series,
  yAxisLabel,
  colorIndexByProduct,
}) => {
  const hasData = series.some((item) =>
    item.values.some((value) => value !== 0),
  );

  if (!hasData) {
    return (
      <div className="flex items-center justify-center h-96">
        <div className="text-center text-gray-500 dark:text-graphite-muted">
          <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300 dark:text-graphite-faint" />
          <p>Žádná data pro zobrazení grafu</p>
          <p className="text-sm">
            Vybrané produkty nemají v tomto období žádný pohyb.
          </p>
        </div>
      </div>
    );
  }

  const chartData = {
    labels: months,
    datasets: series.map((item, index) => {
      const color = getSeriesColor(
        colorIndexByProduct?.get(item.productCode) ?? index,
      );

      return {
        label: `${item.productName} (${item.productCode})`,
        data: item.values,
        borderColor: color.border,
        backgroundColor: color.background,
        borderWidth: 2,
        tension: 0.1,
        pointRadius: 3,
        pointHoverRadius: 5,
      };
    }),
  };

  const chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: "index" as const,
      intersect: false,
    },
    plugins: {
      legend: {
        position: "top" as const,
      },
      title: {
        display: false,
      },
    },
    scales: {
      y: {
        beginAtZero: true,
        title: {
          display: true,
          text: yAxisLabel,
        },
      },
      x: {
        title: {
          display: true,
          text: "Měsíc",
        },
      },
    },
  };

  return (
    <div className="h-96">
      <Line data={chartData} options={chartOptions} />
    </div>
  );
};

export default ProductStatisticsChart;
