import React, { useState } from "react";
import { BarChart3 } from "lucide-react";
import {
  useProductStatistics,
  ProductStatisticsMetric,
  StatisticsMetric,
} from "../../api/hooks/useProductStatistics";
import ProductStatisticsFilter, {
  SelectedProduct,
  defaultDateFrom,
  defaultDateTo,
} from "../product-statistics/ProductStatisticsFilter";
import ProductStatisticsChart, {
  ProductStatisticsSeries,
} from "../product-statistics/ProductStatisticsChart";
import ProductStatisticsTable from "../product-statistics/ProductStatisticsTable";
import LoadingState from "../common/LoadingState";
import ErrorState from "../common/ErrorState";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";

interface MetricTab {
  metric: StatisticsMetric;
  label: string;
  yAxisLabel: string;
}

const METRIC_TABS: MetricTab[] = [
  { metric: ProductStatisticsMetric.Sales, label: "Prodeje", yAxisLabel: "Kusů prodáno" },
  { metric: ProductStatisticsMetric.Purchase, label: "Nákupy", yAxisLabel: "Kusů nakoupeno" },
  {
    metric: ProductStatisticsMetric.Consumption,
    label: "Spotřeba",
    yAxisLabel: "Množství spotřebováno",
  },
  { metric: ProductStatisticsMetric.Manufacture, label: "Výroba", yAxisLabel: "Kusů vyrobeno" },
];

const ProductStatistics: React.FC = () => {
  useScreenView("Catalog", "ProductStatistics");

  // Filter state is shared across tabs: switching metric keeps the selection and range.
  const [selectedProducts, setSelectedProducts] = useState<SelectedProduct[]>(
    [],
  );
  const [dateFrom, setDateFrom] = useState<string>(defaultDateFrom());
  const [dateTo, setDateTo] = useState<string>(defaultDateTo());
  const [activeMetric, setActiveMetric] = useState<StatisticsMetric>(
    ProductStatisticsMetric.Sales,
  );

  const activeTab =
    METRIC_TABS.find((tab) => tab.metric === activeMetric) ?? METRIC_TABS[0];

  const productCodes = selectedProducts.map((product) => product.productCode);

  const { data, isLoading, isError } = useProductStatistics(
    productCodes,
    activeMetric,
    dateFrom,
    dateTo,
  );

  const months: string[] = data?.months ?? [];
  const series: ProductStatisticsSeries[] = (data?.products ?? []).map(
    (product: any) => ({
      productCode: product.productCode,
      productName: product.productName,
      values: product.values ?? [],
    }),
  );

  const hasSelection = selectedProducts.length > 0;

  const renderContent = () => {
    if (!hasSelection) {
      return (
        <div className="flex items-center justify-center h-96">
          <div className="text-center text-gray-500 dark:text-graphite-muted">
            <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300 dark:text-graphite-faint" />
            <p>Vyberte produkty pro zobrazení statistik</p>
          </div>
        </div>
      );
    }

    if (isLoading) {
      return <LoadingState />;
    }

    if (isError) {
      return <ErrorState message="Nepodařilo se načíst statistiky produktů" />;
    }

    return (
      <>
        <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4 mb-4">
          <ProductStatisticsChart
            months={months}
            series={series}
            yAxisLabel={activeTab.yAxisLabel}
          />
        </div>
        <ProductStatisticsTable months={months} series={series} />
      </>
    );
  };

  return (
    <div className={`flex flex-col ${PAGE_CONTAINER_HEIGHT}`}>
      <h1 className="text-xl font-semibold text-gray-900 dark:text-graphite-text mb-4">
        Statistiky produktů
      </h1>

      <ProductStatisticsFilter
        selectedProducts={selectedProducts}
        onProductsChange={setSelectedProducts}
        dateFrom={dateFrom}
        dateTo={dateTo}
        onDateFromChange={setDateFrom}
        onDateToChange={setDateTo}
      />

      <div className="flex border-b border-gray-200 dark:border-graphite-border mb-4">
        {METRIC_TABS.map((tab) => (
          <button
            key={tab.metric}
            onClick={() => setActiveMetric(tab.metric)}
            className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeMetric === tab.metric
                ? "border-indigo-500 text-indigo-600 dark:text-graphite-accent dark:border-graphite-accent"
                : "border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto">{renderContent()}</div>
    </div>
  );
};

export default ProductStatistics;
