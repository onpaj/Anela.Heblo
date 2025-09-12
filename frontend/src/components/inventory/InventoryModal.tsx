import React, { useState, useEffect } from "react";
import { X, Package, Minus, Plus, Loader2, AlertCircle, CheckCircle, History } from "lucide-react";
import { CatalogItemDto, useCatalogDetail } from "../../api/hooks/useCatalog";
import { useSubmitStockTaking, useStockTakingHistory } from "../../api/hooks/useStockTaking";
import { useToast } from "../../contexts/ToastContext";

interface InventoryModalProps {
  item: CatalogItemDto | null;
  isOpen: boolean;
  onClose: () => void;
}

const InventoryModal: React.FC<InventoryModalProps> = ({
  item,
  isOpen,
  onClose,
}) => {
  const [newQuantity, setNewQuantity] = useState<number>(0);
  const [activeTab, setActiveTab] = useState<'inventory' | 'history'>('inventory');
  
  // Stock taking mutation hook
  const submitStockTaking = useSubmitStockTaking();
  const { showSuccess } = useToast();

  // Fetch detailed data when modal is open and item is selected
  const {
    data: detailData,
    isLoading: detailLoading,
    error: detailError,
  } = useCatalogDetail(
    item?.productCode || "",
    1 // Just need current data, not history
  );

  // Use detailed item data if available, fallback to prop item
  const effectiveItem = detailData?.item || item;

  // Stock taking history hook - enabled only when item is available
  const {
    data: historyData,
    isLoading: historyLoading,
    error: historyError,
  } = useStockTakingHistory({
    productCode: effectiveItem?.productCode,
    pageNumber: 1,
    pageSize: 20,
  });

  // Reset quantity when effective item changes (either from prop or API)
  useEffect(() => {
    if (effectiveItem) {
      const currentStock = Math.round((effectiveItem.stock?.available || 0) * 100) / 100;
      setNewQuantity(currentStock);
    }
  }, [effectiveItem]);

  // Reset mutation state when modal opens
  useEffect(() => {
    if (isOpen) {
      submitStockTaking.reset();
    }
  }, [isOpen, submitStockTaking]);

  // Handle ESC key to close modal
  useEffect(() => {
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

  if (!isOpen || !item) return null;

  const currentStock = Math.round((effectiveItem?.stock?.available || 0) * 100) / 100;

  const handleInventorize = async () => {
    if (!effectiveItem?.productCode) return;

    const currentStock = Math.round((effectiveItem?.stock?.available || 0) * 100) / 100;
    
    // Determine if this is a soft stock taking (no change in quantity)
    const isSoftStockTaking = newQuantity === currentStock;
    
    try {
      await submitStockTaking.mutateAsync({
        productCode: effectiveItem.productCode,
        targetAmount: newQuantity,
        softStockTaking: isSoftStockTaking,
      });
      
      // Show success toaster only if stock actually changed
      if (!isSoftStockTaking) {
        const difference = newQuantity - currentStock;
        const differenceText = difference > 0 ? `+${difference.toFixed(2)}` : difference.toFixed(2);
        
        showSuccess(
          "Inventarizace dokončena",
          `Množství produktu ${effectiveItem.productCode} bylo aktualizováno (${differenceText})`,
          { duration: 5000 }
        );
      }
      
      // Close modal on success
      onClose();
    } catch (error) {
      console.error("Stock taking failed:", error);
      // Error is handled by the mutation hook
    }
  };

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={onClose}
      />

      {/* Modal */}
      <div className="flex min-h-full items-center justify-center p-4 text-center sm:p-0">
        <div className="relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-6xl">
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
            <h3 className="text-lg font-semibold text-gray-900 flex items-center">
              <Package className="h-5 w-5 mr-2 text-indigo-600" />
              Inventarizace produktu
            </h3>
            <button
              onClick={onClose}
              className="rounded-md text-gray-400 hover:text-gray-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
            >
              <X className="h-6 w-6" />
            </button>
          </div>

          {/* Tab Navigation */}
          <div className="flex border-b border-gray-200">
            <button
              onClick={() => setActiveTab('inventory')}
              className={`px-6 py-3 text-sm font-medium border-b-2 transition-colors ${
                activeTab === 'inventory'
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              <Package className="h-4 w-4 inline mr-2" />
              Inventura
            </button>
            <button
              onClick={() => setActiveTab('history')}
              className={`px-6 py-3 text-sm font-medium border-b-2 transition-colors ${
                activeTab === 'history'
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              <History className="h-4 w-4 inline mr-2" />
              Log
            </button>
          </div>

          {/* Product Name - Full Width */}
          <div className="px-6 py-4 bg-gray-50 border-b border-gray-200">
            <h4 className="text-2xl font-bold text-gray-900 text-center">
              {effectiveItem?.productName || item.productName}
            </h4>
          </div>

          {/* Tab Content */}
          {activeTab === 'inventory' ? (
            <div className="flex flex-col lg:flex-row min-h-[500px]">
              {/* Loading State */}
              {detailLoading && (
                <div className="w-full flex items-center justify-center">
                  <div className="flex items-center space-x-2">
                    <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                    <div className="text-gray-500">Načítání detailních údajů...</div>
                  </div>
                </div>
              )}

              {/* Error State */}
              {detailError && (
                <div className="w-full flex items-center justify-center">
                  <div className="text-red-600">
                    Chyba při načítání detailů: {detailError.message}
                  </div>
                </div>
              )}

              {/* Content - Show when not loading */}
              {!detailLoading && !detailError && (
              <>
                {/* Left Section - Product Image (50%) */}
                <div className="w-full lg:w-1/2 p-6 bg-gray-50">
                  <div className="h-full flex items-center justify-center">
                    {/* Product Image */}
                    <div className="w-full">
                      <div className="w-full bg-gray-200 rounded-lg overflow-hidden flex items-center justify-center min-h-[24rem]">
                        {effectiveItem?.image ? (
                          <img 
                            src={effectiveItem.image} 
                            alt={effectiveItem.productName || "Obrázek produktu"}
                            className="max-w-full max-h-full object-contain"
                            onError={(e) => {
                              // Fallback to placeholder if image fails to load
                              const target = e.target as HTMLImageElement;
                              target.style.display = 'none';
                              const placeholder = target.nextElementSibling as HTMLElement;
                              if (placeholder) placeholder.style.display = 'flex';
                            }}
                          />
                        ) : null}
                        <div 
                          className={`w-full h-full flex items-center justify-center ${effectiveItem?.image ? 'hidden' : 'flex'}`}
                          style={{ display: effectiveItem?.image ? 'none' : 'flex' }}
                        >
                          <div className="text-center text-gray-500">
                            <Package className="h-12 w-12 mx-auto mb-2 text-gray-400" />
                            <p className="text-sm">Obrázek produktu</p>
                            <p className="text-xs">není dostupný</p>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Right Section - Product Info & Quantity Management (50%) */}
                <div className="w-full lg:w-1/2 p-6 bg-white">
                  <div className="space-y-6">
                    {/* Product Info */}
                    <div className="space-y-3 pb-4 border-b border-gray-200">
                      {/* Product Code */}
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600">
                          Produktový kód:
                        </span>
                        <span className="text-sm text-gray-900 font-mono">
                          {effectiveItem?.productCode}
                        </span>
                      </div>

                      {/* Location */}
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600">
                          Pozice:
                        </span>
                        <span className="text-sm text-gray-900">
                          {effectiveItem?.location || "Není uvedeno"}
                        </span>
                      </div>
                    </div>

                    {/* Current Stock Display */}
                    <div className="text-center">
                      <div className="text-sm font-medium text-gray-600 mb-2">
                        Aktuální množství skladem
                      </div>
                      <div className="text-6xl font-bold text-green-600 mb-4">
                        {currentStock}
                      </div>
                    </div>

                    {/* Quantity Input */}
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-2">
                        Nové množství po inventarizaci
                      </label>
                      <div className="flex items-center space-x-3 mb-4">
                        <button
                          onClick={() => setNewQuantity(Math.max(0, newQuantity - 1))}
                          className="w-32 h-12 flex items-center justify-center bg-white border border-gray-300 rounded-lg text-gray-600 hover:bg-gray-50 hover:text-gray-900 touch-manipulation text-xl font-semibold"
                          type="button"
                        >
                          <Minus className="h-5 w-5" />
                        </button>
                        <input
                          type="number"
                          min="0"
                          step="0.01"
                          value={newQuantity}
                          onChange={(e) => {
                            const value = parseFloat(e.target.value);
                            setNewQuantity(isNaN(value) ? 0 : Math.max(0, value));
                          }}
                          className="flex-1 text-center border border-gray-300 rounded-lg px-4 py-3 text-xl font-bold focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent touch-manipulation"
                        />
                        <button
                          onClick={() => setNewQuantity(newQuantity + 1)}
                          className="w-32 h-12 flex items-center justify-center bg-white border border-gray-300 rounded-lg text-gray-600 hover:bg-gray-50 hover:text-gray-900 touch-manipulation text-xl font-semibold"
                          type="button"
                        >
                          <Plus className="h-5 w-5" />
                        </button>
                      </div>
                    </div>

                    {/* Inventory Button */}
                    <div className="pt-6">
                      <button
                        onClick={handleInventorize}
                        disabled={submitStockTaking.isPending || !effectiveItem?.productCode}
                        className="w-full bg-indigo-600 hover:bg-indigo-700 disabled:bg-gray-400 disabled:cursor-not-allowed text-white text-lg font-semibold py-4 px-6 rounded-lg transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 flex items-center justify-center space-x-2"
                      >
                        {submitStockTaking.isPending ? (
                          <>
                            <Loader2 className="h-5 w-5 animate-spin" />
                            <span>Inventarizuji...</span>
                          </>
                        ) : (
                          <span>Zinventarizovat</span>
                        )}
                      </button>
                    </div>

                    {/* Error Message */}
                    {submitStockTaking.error && (
                      <div className="mt-4 p-3 bg-red-50 rounded-lg border border-red-200 flex items-start space-x-2">
                        <AlertCircle className="h-5 w-5 text-red-600 mt-0.5 flex-shrink-0" />
                        <div>
                          <div className="text-sm font-medium text-red-800">
                            Chyba při inventarizaci
                          </div>
                          <div className="text-sm text-red-700 mt-1">
                            {submitStockTaking.error?.message || "Došlo k neočekávané chybě"}
                          </div>
                        </div>
                      </div>
                    )}

                    {/* Success Message */}
                    {submitStockTaking.isSuccess && (
                      <div className="mt-4 p-3 bg-green-50 rounded-lg border border-green-200 flex items-start space-x-2">
                        <CheckCircle className="h-5 w-5 text-green-600 mt-0.5 flex-shrink-0" />
                        <div className="text-sm font-medium text-green-800">
                          Inventarizace byla úspěšně dokončena
                        </div>
                      </div>
                    )}

                    {/* Difference Display */}
                    {newQuantity !== currentStock && (
                      <div className="mt-4 p-3 bg-yellow-50 rounded-lg border border-yellow-200">
                        <div className="text-center">
                          <span className="text-sm font-medium text-yellow-800">
                            Rozdíl: {newQuantity > currentStock ? "+" : ""}{(newQuantity - currentStock).toFixed(2)}
                          </span>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </>
              )}
            </div>
          ) : (
            /* History Tab Content */
            <div className="min-h-[500px] p-6">
              {/* History Loading State */}
              {historyLoading && (
                <div className="flex items-center justify-center h-64">
                  <div className="flex items-center space-x-2">
                    <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                    <div className="text-gray-500">Načítání historie inventur...</div>
                  </div>
                </div>
              )}

              {/* History Error State */}
              {historyError && (
                <div className="flex items-center justify-center h-64">
                  <div className="text-red-600">
                    Chyba při načítání historie: {historyError.message}
                  </div>
                </div>
              )}

              {/* History Content */}
              {!historyLoading && !historyError && historyData && (
                <div>
                  <div className="mb-4">
                    <h5 className="text-lg font-semibold text-gray-900">
                      Historie inventur ({historyData.totalCount || 0})
                    </h5>
                  </div>

                  {historyData.items && historyData.items.length > 0 ? (
                    <div className="overflow-x-auto">
                      <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                          <tr>
                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                              Datum
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                              Staré množství
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                              Nové množství
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                              Rozdíl
                            </th>
                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                              Uživatel
                            </th>
                          </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                          {historyData.items?.map((record, index) => (
                            <tr key={record.id || index} className="hover:bg-gray-50">
                              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                {record.date ? new Date(record.date).toLocaleString('cs-CZ') : 'N/A'}
                              </td>
                              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                {record.amountOld !== undefined ? record.amountOld.toFixed(2) : 'N/A'}
                              </td>
                              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                {record.amountNew !== undefined ? record.amountNew.toFixed(2) : 'N/A'}
                              </td>
                              <td className="px-6 py-4 whitespace-nowrap text-sm">
                                <span className={`${
                                  (record.difference || 0) > 0 
                                    ? 'text-green-600' 
                                    : (record.difference || 0) < 0 
                                    ? 'text-red-600' 
                                    : 'text-gray-600'
                                }`}>
                                  {(record.difference || 0) > 0 ? '+' : ''}{record.difference !== undefined ? record.difference.toFixed(2) : '0.00'}
                                </span>
                              </td>
                              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                {record.user || 'N/A'}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div className="text-center py-8">
                      <History className="h-12 w-12 mx-auto text-gray-400 mb-4" />
                      <p className="text-gray-500">
                        Žádné záznamy inventur pro tento produkt
                      </p>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default InventoryModal;