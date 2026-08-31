import React, { useState } from "react";
import { BarChart3, RefreshCw } from "lucide-react";
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
import { assignSeriesColors } from "../product-statistics/productStatisticsColors";
import { ProductStatisticsSeriesDto } from "../../api/generated/api-client";
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
  // Palette slots live beside the selection so a removal never recolors the survivors.
  const [colorIndexByProduct, setColorIndexByProduct] = useState<
    ReadonlyMap<string, number>
  >(new Map());

  const [dateFrom, setDateFrom] = useState<string>(defaultDateFrom());
  const [dateTo, setDateTo] = useState<string>(defaultDateTo());
  const [activeMetric, setActiveMetric] = useState<StatisticsMetric>(
    ProductStatisticsMetric.Sales,
  );

  const handleProductsChange = (products: SelectedProduct[]) => {
    setSelectedProducts(products);
    setColorIndexByProduct((previous) =>
      assignSeriesColors(
        products.map((product) => product.productCode),
        previous,
      ),
    );
  };

  const activeTab =
    METRIC_TABS.find((tab) => tab.metric === activeMetric) ?? METRIC_TABS[0];

  const productCodes = selectedProducts.map((product) => product.productCode);

  const { data, isLoading, isError, refetch } = useProductStatistics(
    productCodes,
    activeMetric,
    dateFrom,
    dateTo,
  );

  const months: string[] = data?.months ?? [];
  // Every generated DTO property is optional, so a series missing its code is dropped
  // rather than rendered — it would otherwise become key={undefined} in the table.
  const series: ProductStatisticsSeries[] = (data?.products ?? [])
    .filter((product: ProductStatisticsSeriesDto) => Boolean(product.productCode))
    .map((product: ProductStatisticsSeriesDto) => ({
      productCode: product.productCode as string,
      productName: product.productName ?? (product.productCode as string),
      values: product.values ?? [],
    }));

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
      return (
        <div className="flex flex-col items-center justify-center h-64">
          <ErrorState
            message="Nepodařilo se načíst statistiky produktů"
            className="h-auto"
          />
          <button
            type="button"
            onClick={() => refetch()}
            className="mt-3 inline-flex items-center px-3 py-1.5 text-sm font-medium text-gray-700 dark:text-graphite-text bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-graphite-surface-2"
          >
            <RefreshCw className="h-4 w-4 mr-1.5" />
            Zkusit znovu
          </button>
        </div>
      );
    }

    return (
      <>
        <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4 mb-4">
          <ProductStatisticsChart
            months={months}
            series={series}
            yAxisLabel={activeTab.yAxisLabel}
            colorIndexByProduct={colorIndexByProduct}
          />
        </div>
        <ProductStatisticsTable months={months} series={series} />
      </>
    );
  };

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Statistiky produktů
        </h1>
      </div>

      <ProductStatisticsFilter
        selectedProducts={selectedProducts}
        onProductsChange={handleProductsChange}
        dateFrom={dateFrom}
        dateTo={dateTo}
        onDateFromChange={setDateFrom}
        onDateToChange={setDateTo}
      />

      <div
        role="tablist"
        aria-label="Metrika"
        className="flex-shrink-0 flex border-b border-gray-200 dark:border-graphite-border mb-4"
      >
        {METRIC_TABS.map((tab) => (
          <button
            key={tab.metric}
            type="button"
            role="tab"
            aria-selected={activeMetric === tab.metric}
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

      <div className="flex-1 bg-white dark:bg-graphite-surface shadow rounded-lg overflow-auto min-h-0 p-4">
        {renderContent()}
      </div>
    </div>
  );
};

export default ProductStatistics;
