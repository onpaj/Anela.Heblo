import React from "react";
import {
  FileText,
  ShoppingCart,
  TrendingUp,
  BookOpen,
  ArrowRight,
} from "lucide-react";
import { CatalogItemDto, ProductType } from "../../../api/hooks/useCatalog";
import { JournalEntryDto } from "../../../api/generated/api-client";
import BasicInfoTab from "./tabs/BasicInfoTab/BasicInfoTab";
import PurchaseHistoryTab from "./tabs/PurchaseHistoryTab";
import MarginsTab from "./tabs/MarginsTab/MarginsTab";
import JournalTab from "./tabs/JournalTab";
import UsageTab from "./tabs/UsageTab";

interface CatalogDetailTabsProps {
  item: CatalogItemDto;
  activeTab: "basic" | "history" | "margins" | "journal" | "usage";
  onTabChange: (
    tab: "basic" | "history" | "margins" | "journal" | "usage",
  ) => void;
  detailData: any;
  isLoading: boolean;
  journalEntries: JournalEntryDto[];
  onManufactureDifficultyClick: () => void;
  onAddJournalEntry: () => void;
  onEditJournalEntry: (entry: JournalEntryDto) => void;
  onViewAllEntries: () => void;
}

const CatalogDetailTabs: React.FC<CatalogDetailTabsProps> = ({
  item,
  activeTab,
  onTabChange,
  detailData,
  isLoading,
  journalEntries,
  onManufactureDifficultyClick,
  onAddJournalEntry,
  onEditJournalEntry,
  onViewAllEntries,
}) => {
  return (
    <div className="flex flex-col overflow-hidden">
      {/* Tab Navigation */}
      <div className="flex border-b border-gray-200 mb-6">
        <button
          onClick={() => onTabChange("basic")}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === "basic"
              ? "border-indigo-500 text-indigo-600"
              : "border-transparent text-gray-500 hover:text-gray-700"
          }`}
        >
          <FileText className="h-4 w-4" />
          <span>Základní informace</span>
        </button>

        {/* Historie nákupů - pouze pro Material a Goods */}
        {(item?.type === ProductType.Material ||
          item?.type === ProductType.Goods) && (
          <button
            onClick={() => onTabChange("history")}
            className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
              activeTab === "history"
                ? "border-indigo-500 text-indigo-600"
                : "border-transparent text-gray-500 hover:text-gray-700"
            }`}
          >
            <ShoppingCart className="h-4 w-4" />
            <span>Historie nákupů</span>
          </button>
        )}

        {(item?.type === ProductType.Product ||
          item?.type === ProductType.SemiProduct ||
          item?.type === ProductType.Goods) && (
          <button
            onClick={() => onTabChange("margins")}
            className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
              activeTab === "margins"
                ? "border-indigo-500 text-indigo-600"
                : "border-transparent text-gray-500 hover:text-gray-700"
            }`}
          >
            <TrendingUp className="h-4 w-4" />
            <span>Marže</span>
          </button>
        )}

        <button
          onClick={() => onTabChange("journal")}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === "journal"
              ? "border-indigo-500 text-indigo-600"
              : "border-transparent text-gray-500 hover:text-gray-700"
          }`}
        >
          <BookOpen className="h-4 w-4" />
          <span>Deník</span>
        </button>

        {/* Použití tab - pouze pro SemiProduct a Material */}
        {(item?.type === ProductType.SemiProduct ||
          item?.type === ProductType.Material) && (
          <button
            onClick={() => onTabChange("usage")}
            className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
              activeTab === "usage"
                ? "border-indigo-500 text-indigo-600"
                : "border-transparent text-gray-500 hover:text-gray-700"
            }`}
          >
            <ArrowRight className="h-4 w-4" />
            <span>Použití</span>
          </button>
        )}
      </div>

      {/* Tab Content */}
      <div className="flex-1 overflow-y-auto">
        {activeTab === "basic" ? (
          <BasicInfoTab
            item={item}
            onManufactureDifficultyClick={onManufactureDifficultyClick}
          />
        ) : activeTab === "history" ? (
          <PurchaseHistoryTab
            purchaseHistory={detailData?.historicalData?.purchaseHistory || []}
            isLoading={isLoading}
          />
        ) : activeTab === "margins" ? (
          <MarginsTab
            item={item}
            manufactureCostHistory={
              detailData?.historicalData?.manufactureCostHistory || []
            }
            marginHistory={detailData?.historicalData?.marginHistory || []}
            isLoading={isLoading}
            journalEntries={journalEntries}
          />
        ) : activeTab === "usage" ? (
          <UsageTab productCode={item.productCode || ""} />
        ) : (
          <JournalTab
            productCode={item.productCode || ""}
            onAddEntry={onAddJournalEntry}
            onEditEntry={onEditJournalEntry}
            onViewAllEntries={onViewAllEntries}
          />
        )}
      </div>
    </div>
  );
};

export default CatalogDetailTabs;
