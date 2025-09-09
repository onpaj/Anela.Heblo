import React from "react";
import { BarChart3 } from "lucide-react";
import { CatalogItemDto, ProductType } from "../../../api/hooks/useCatalog";
import { JournalEntryDto } from "../../../api/generated/api-client";
import {
  shouldShowChartTabs,
  getInputTabName,
  getOutputTabName,
} from "./CatalogDetailTypes";
import ProductSummaryTabs from "./charts/ProductSummaryTabs";
import ProductChart from "./charts/ProductChart";

interface CatalogDetailChartSectionProps {
  item: CatalogItemDto;
  activeChartTab: "input" | "output";
  onChartTabChange: (tab: "input" | "output") => void;
  detailData: any;
  journalEntries: JournalEntryDto[];
}

const CatalogDetailChartSection: React.FC<CatalogDetailChartSectionProps> = ({
  item,
  activeChartTab,
  onChartTabChange,
  detailData,
  journalEntries,
}) => {
  const productType = item.type || ProductType.UNDEFINED;

  return (
    <div className="space-y-4">
      <div className="h-full flex flex-col">
        {shouldShowChartTabs(productType) ? (
          <>
            {/* Chart Tab Navigation */}
            <div className="flex border-b border-gray-200 mb-4">
              <button
                onClick={() => onChartTabChange("input")}
                className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                  activeChartTab === "input"
                    ? "border-indigo-500 text-indigo-600"
                    : "border-transparent text-gray-500 hover:text-gray-700"
                }`}
              >
                <BarChart3 className="h-4 w-4" />
                <span>{getInputTabName(productType)}</span>
              </button>
              <button
                onClick={() => onChartTabChange("output")}
                className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
                  activeChartTab === "output"
                    ? "border-indigo-500 text-indigo-600"
                    : "border-transparent text-gray-500 hover:text-gray-700"
                }`}
              >
                <BarChart3 className="h-4 w-4" />
                <span>{getOutputTabName(productType)}</span>
              </button>
            </div>

            {/* Summary Section */}
            <ProductSummaryTabs
              productType={productType}
              activeTab={activeChartTab}
              salesData={detailData?.historicalData?.salesHistory || []}
              consumedData={detailData?.historicalData?.consumedHistory || []}
              purchaseData={detailData?.historicalData?.purchaseHistory || []}
              manufactureData={
                detailData?.historicalData?.manufactureHistory || []
              }
            />

            {/* Chart Content */}
            <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
              <ProductChart
                productType={productType}
                activeTab={activeChartTab}
                salesData={detailData?.historicalData?.salesHistory || []}
                consumedData={detailData?.historicalData?.consumedHistory || []}
                purchaseData={detailData?.historicalData?.purchaseHistory || []}
                manufactureData={
                  detailData?.historicalData?.manufactureHistory || []
                }
                journalEntries={journalEntries}
              />
            </div>
          </>
        ) : (
          <>
            {/* Original behavior for UNDEFINED type */}
            <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
              <BarChart3 className="h-5 w-5 mr-2 text-gray-500" />
              Graf není k dispozici
            </h3>
            <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4 flex items-center justify-center">
              <div className="text-center text-gray-500">
                <BarChart3 className="h-12 w-12 mx-auto mb-2 text-gray-300" />
                <p>Pro tento typ produktu není graf k dispozici</p>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
};

export default CatalogDetailChartSection;
