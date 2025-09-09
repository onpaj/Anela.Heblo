import React, { useState } from "react";
import { X, Package, Loader2, AlertCircle } from "lucide-react";
import { CatalogItemDto, useCatalogDetail } from "../../api/hooks/useCatalog";
import { JournalEntryDto } from "../../api/generated/api-client";
import { useJournalEntriesByProduct } from "../../api/hooks/useJournal";
import { useNavigate } from "react-router-dom";
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
  LineElement,
  PointElement,
} from "chart.js";
import CatalogDetailTabs from "../catalog/detail/CatalogDetailTabs";
import CatalogDetailChartSection from "../catalog/detail/CatalogDetailChartSection";
import CatalogDetailModals from "../catalog/detail/CatalogDetailModals";

ChartJS.register(
  CategoryScale,
  LinearScale,
  BarElement,
  Title,
  Tooltip,
  Legend,
  LineElement,
  PointElement,
);

interface CatalogDetailProps {
  item?: CatalogItemDto | null;
  productCode?: string | null;
  isOpen: boolean;
  onClose: () => void;
  defaultTab?: "basic" | "history" | "margins" | "usage";
}

const CatalogDetail: React.FC<CatalogDetailProps> = ({
  item,
  productCode,
  isOpen,
  onClose,
  defaultTab = "basic",
}) => {
  const [activeTab, setActiveTab] = useState<
    "basic" | "history" | "margins" | "journal" | "usage"
  >(defaultTab as any);
  const [activeChartTab, setActiveChartTab] = useState<"input" | "output">(
    "output",
  );
  const [showJournalModal, setShowJournalModal] = useState(false);
  const [selectedJournalEntry, setSelectedJournalEntry] = useState<
    JournalEntryDto | undefined
  >(undefined);
  const [showManufactureDifficultyModal, setShowManufactureDifficultyModal] =
    useState(false);
  const navigate = useNavigate();

  // Determine which productCode to use - from prop or from item
  const effectiveProductCode = productCode || item?.productCode || "";

  // Fetch detailed data from API - always use full history (999 months)
  const monthsBack = 999;
  const {
    data: detailData,
    isLoading: detailLoading,
    error: detailError,
    refetch: refetchCatalogDetail,
  } = useCatalogDetail(effectiveProductCode, monthsBack);

  // Use item from prop if provided, otherwise use item from API detail data
  const effectiveItem = item || detailData?.item;

  // Fetch journal entries for the product
  const { data: journalData } =
    useJournalEntriesByProduct(effectiveProductCode);

  // Reset tab and chart state when modal opens with new item or different default tab
  React.useEffect(() => {
    if (isOpen) {
      setActiveTab(defaultTab);
      setActiveChartTab("output"); // Default to output tab (sales/consumption)
    }
  }, [isOpen, defaultTab, effectiveProductCode]);

  // Add keyboard event listener for Esc key
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
    }

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, onClose]);

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  if (!isOpen || (!effectiveItem && !detailLoading)) {
    return null;
  }

  // Show loading spinner while fetching data
  if (!effectiveItem && detailLoading) {
    return (
      <div
        onClick={handleBackdropClick}
        className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      >
        <div className="bg-white rounded-lg p-8 flex items-center space-x-3">
          <Loader2 className="h-6 w-6 animate-spin text-indigo-500" />
          <span className="text-gray-600">Načítám detail produktu...</span>
        </div>
      </div>
    );
  }

  // Return null if no effective item available
  if (!effectiveItem) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-[95vw] w-full max-h-[95vh] overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                {effectiveItem.productName}
              </h2>
              <p className="text-sm text-gray-500">
                Kód: {effectiveItem.productCode}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto max-h-[calc(95vh-120px)]">
          {detailLoading ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2">
                <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                <div className="text-gray-500">
                  Načítání detailů produktu...
                </div>
              </div>
            </div>
          ) : detailError ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2 text-red-600">
                <AlertCircle className="h-5 w-5" />
                <div>Chyba při načítání detailů: {detailError.message}</div>
              </div>
            </div>
          ) : (
            <>
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 h-full">
                {/* Left Column - Tabbed Content */}
                <CatalogDetailTabs
                  item={effectiveItem}
                  activeTab={activeTab}
                  onTabChange={setActiveTab}
                  detailData={detailData}
                  isLoading={detailLoading}
                  journalEntries={journalData?.entries || []}
                  onManufactureDifficultyClick={() =>
                    setShowManufactureDifficultyModal(true)
                  }
                  onAddJournalEntry={() => setShowJournalModal(true)}
                  onEditJournalEntry={(entry) => {
                    setSelectedJournalEntry(entry);
                    setShowJournalModal(true);
                  }}
                  onViewAllEntries={() => {
                    navigate(
                      `/journal?productCode=${effectiveItem.productCode}`,
                    );
                    onClose();
                  }}
                />

                {/* Right Column - Charts with Tabs */}
                <CatalogDetailChartSection
                  item={effectiveItem}
                  activeChartTab={activeChartTab}
                  onChartTabChange={setActiveChartTab}
                  detailData={detailData}
                  journalEntries={journalData?.entries || []}
                />
              </div>
            </>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end p-6 border-t border-gray-200 bg-gray-50">
          <button
            onClick={onClose}
            className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200"
          >
            Zavřít
          </button>
        </div>
      </div>

      {/* Modals */}
      <CatalogDetailModals
        item={effectiveItem}
        showJournalModal={showJournalModal}
        onCloseJournalModal={() => {
          setShowJournalModal(false);
          setSelectedJournalEntry(undefined);
        }}
        selectedJournalEntry={selectedJournalEntry}
        showManufactureDifficultyModal={showManufactureDifficultyModal}
        onCloseManufactureDifficultyModal={() =>
          setShowManufactureDifficultyModal(false)
        }
        refetchCatalogDetail={refetchCatalogDetail}
      />
    </div>
  );
};

export default CatalogDetail;
