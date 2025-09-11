import React, { useState, useEffect } from "react";
import { X, Package, Minus, Plus, Loader2 } from "lucide-react";
import { CatalogItemDto, useCatalogDetail } from "../../api/hooks/useCatalog";

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

  // Reset quantity when effective item changes (either from prop or API)
  useEffect(() => {
    if (effectiveItem) {
      const currentStock = Math.round((effectiveItem.stock?.available || 0) * 100) / 100;
      setNewQuantity(currentStock);
    }
  }, [effectiveItem]);

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

  const handleInventorize = () => {
    // TODO: Implement inventory update API call
    console.log(`Inventorizing ${item.productCode} to quantity: ${newQuantity}`);
    onClose();
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

          {/* Product Name - Full Width */}
          <div className="px-6 py-4 bg-gray-50 border-b border-gray-200">
            <h4 className="text-2xl font-bold text-gray-900 text-center">
              {effectiveItem?.productName || item.productName}
            </h4>
          </div>

          {/* Content */}
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
                        className="w-full bg-indigo-600 hover:bg-indigo-700 text-white text-lg font-semibold py-4 px-6 rounded-lg transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
                      >
                        Zinventarizovat
                      </button>
                    </div>

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
        </div>
      </div>
    </div>
  );
};

export default InventoryModal;