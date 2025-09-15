import React, { useState, useEffect } from "react";
import { X, Wrench, Loader2, AlertCircle, CheckCircle, History, Plus, Trash2, Minus } from "lucide-react";
import { CatalogItemDto, useCatalogDetail } from "../../api/hooks/useCatalog";
import { useSubmitManufactureStockTaking, useStockTakingHistory } from "../../api/hooks/useManufactureStockTaking";
import { useToast } from "../../contexts/ToastContext";
import { parseLocalDate, formatLocalDate } from "../../utils/dateUtils";

interface EditableLot {
  lotCode: string | null;
  amount: number;
  expiration: Date | null;
  originalAmount: number;
  isNew?: boolean;
}

interface ManufactureInventoryModalProps {
  item: CatalogItemDto | null;
  isOpen: boolean;
  onClose: () => void;
}

const ManufactureInventoryModal: React.FC<ManufactureInventoryModalProps> = ({
  item,
  isOpen,
  onClose,
}) => {
  const [newQuantity, setNewQuantity] = useState<number>(0);
  const [activeTab, setActiveTab] = useState<'inventory' | 'history'>('inventory');
  const [editableLots, setEditableLots] = useState<EditableLot[]>([]);
  
  // Stock taking mutation hook
  const submitStockTaking = useSubmitManufactureStockTaking();
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
      // For materials, use ERP stock instead of eshop stock
      const currentStock = Math.round((effectiveItem.stock?.erp || 0) * 100) / 100;
      setNewQuantity(currentStock);

      // Initialize editable lots if the item has lots
      if (effectiveItem.hasLots && effectiveItem.lots) {
        const lots: EditableLot[] = effectiveItem.lots.map(lot => ({
          lotCode: lot.lotCode || null,
          amount: lot.amount ?? 0,
          expiration: lot.expiration || null,
          originalAmount: lot.amount ?? 0,
        }));
        setEditableLots(lots);
      } else {
        setEditableLots([]);
      }
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

  // For materials, use ERP stock instead of eshop stock
  const currentStock = Math.round((effectiveItem?.stock?.erp || 0) * 100) / 100;

  // Helper functions for lot management
  const updateLotAmount = (index: number, newAmount: number) => {
    const roundedAmount = Math.round(Math.max(0, newAmount) * 100) / 100;
    setEditableLots(prev => prev.map((lot, i) => 
      i === index ? { ...lot, amount: roundedAmount } : lot
    ));
  };

  const updateLotCode = (index: number, newCode: string) => {
    setEditableLots(prev => prev.map((lot, i) => 
      i === index ? { ...lot, lotCode: newCode || null } : lot
    ));
  };

  const updateLotExpiration = (index: number, newExpiration: string) => {
    setEditableLots(prev => prev.map((lot, i) => 
      i === index ? { 
        ...lot, 
        expiration: newExpiration ? parseLocalDate(newExpiration) : null 
      } : lot
    ));
  };

  const addNewLot = () => {
    setEditableLots(prev => [...prev, {
      lotCode: '',
      amount: 0,
      expiration: null,
      originalAmount: 0,
      isNew: true,
    }]);
  };

  const removeLot = (index: number) => {
    setEditableLots(prev => prev.filter((_, i) => i !== index));
  };

  // Calculate total amount from lots
  const totalLotAmount = editableLots.reduce((sum, lot) => sum + lot.amount, 0);

  const handleInventorize = async () => {
    if (!effectiveItem?.productCode) return;

    try {
      if (effectiveItem.hasLots && editableLots.length > 0) {
        // Lot-based stock taking
        const lotsRequest = editableLots.map(lot => ({
          lotCode: lot.lotCode,
          expiration: lot.expiration,
          amount: lot.amount,
          softStockTaking: lot.amount === lot.originalAmount,
        }));

        await submitStockTaking.mutateAsync({
          productCode: effectiveItem.productCode,
          lots: lotsRequest,
        });

        // Show success message for lot-based inventory
        const changedLots = editableLots.filter(lot => lot.amount !== lot.originalAmount);
        if (changedLots.length > 0) {
          const totalDifference = totalLotAmount - (effectiveItem?.lots?.reduce((sum, lot) => sum + (lot.amount ?? 0), 0) || 0);
          const differenceText = totalDifference > 0 ? `+${totalDifference.toFixed(2)}` : totalDifference.toFixed(2);
          
          showSuccess(
            "Inventarizace materiálu dokončena",
            `Množství materiálu ${effectiveItem.productCode} bylo aktualizováno (${differenceText}). Změněno ${changedLots.length} sarží.`,
            { duration: 5000 }
          );
        }
      } else {
        // Simple stock taking
        const currentStock = Math.round((effectiveItem?.stock?.erp || 0) * 100) / 100;
        const isSoftStockTaking = newQuantity === currentStock;
        
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
            "Inventarizace materiálu dokončena",
            `Množství materiálu ${effectiveItem.productCode} bylo aktualizováno (${differenceText})`,
            { duration: 5000 }
          );
        }
      }
      
      // Close modal on success
      onClose();
    } catch (error) {
      console.error("Manufacture stock taking failed:", error);
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
              <Wrench className="h-5 w-5 mr-2 text-indigo-600" />
              Inventarizace materiálu
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
              <Wrench className="h-4 w-4 inline mr-2" />
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

          {/* Warning for pending orders */}
          {effectiveItem?.stock?.ordered && effectiveItem.stock.ordered > 0 && (
            <div className="px-6 py-3 bg-red-600 border-b border-red-700">
              <div className="flex items-center justify-center space-x-2">
                <AlertCircle className="h-5 w-5 text-white flex-shrink-0" />
                <div className="text-white text-center font-medium">
                  <strong>Upozornění:</strong> Tento produkt je součástí nákupní objednávky (objednané množství: {effectiveItem.stock.ordered.toFixed(2)})
                </div>
              </div>
            </div>
          )}

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
                {/* Left Section - Lots Management (2/3 for lots, full width for no lots) */}
                <div className={`${effectiveItem?.hasLots ? 'w-full lg:w-2/3' : 'w-full'} p-6 bg-gray-50`}>
                  <div className="h-full flex items-center justify-center">
                    {effectiveItem?.hasLots ? (
                      /* Lots List */
                      <div className="w-full">
                        <div className="mb-4">
                          <h5 className="text-lg font-semibold text-gray-900 text-center">
                            Aktuální sarže
                          </h5>
                        </div>
                        <div className="w-full bg-white rounded-lg border border-gray-300 min-h-[20rem] max-h-[24rem] overflow-y-auto">
                          {editableLots && editableLots.length > 0 ? (
                            <div className="space-y-2 p-4">
                              {editableLots.map((lot, index) => (
                                <div key={lot.lotCode || index} className="flex items-center space-x-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
                                  {/* Lot Code Input - smaller */}
                                  <div className="w-32">
                                    <input
                                      type="text"
                                      value={lot.lotCode || ''}
                                      onChange={(e) => updateLotCode(index, e.target.value)}
                                      placeholder={lot.isNew ? "Sarže" : "Bez kódu"}
                                      className="w-full text-sm border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                                      readOnly={!lot.isNew}
                                      title={lot.isNew ? "Zadejte kód sarže" : "Kód sarže (jen pro čtení)"}
                                    />
                                  </div>

                                  {/* Expiration Date Input - smaller */}
                                  <div className="w-40">
                                    <input
                                      type="date"
                                      value={lot.expiration ? formatLocalDate(lot.expiration) : ''}
                                      onChange={(e) => updateLotExpiration(index, e.target.value)}
                                      className="w-full text-sm border border-gray-300 rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                                      readOnly={!lot.isNew}
                                      title={lot.isNew ? "Datum expirace" : "Datum expirace (jen pro čtení)"}
                                    />
                                  </div>

                                  {/* Amount Input with Buttons - larger space */}
                                  <div className="flex items-center space-x-1 flex-1">
                                    <button
                                      onClick={() => updateLotAmount(index, lot.amount - 1)}
                                      className="w-8 h-8 flex items-center justify-center bg-gray-100 border border-gray-300 rounded text-gray-600 hover:bg-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                                      type="button"
                                      title="Snížit množství"
                                    >
                                      <Minus className="h-3 w-3" />
                                    </button>
                                    <input
                                      type="number"
                                      min="0"
                                      step="0.01"
                                      value={lot.amount.toFixed(2)}
                                      onChange={(e) => {
                                        const value = parseFloat(e.target.value);
                                        updateLotAmount(index, isNaN(value) ? 0 : value);
                                      }}
                                      className="flex-1 text-center border border-gray-300 rounded px-2 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent min-w-0"
                                      title="Množství"
                                    />
                                    <button
                                      onClick={() => updateLotAmount(index, lot.amount + 1)}
                                      className="w-8 h-8 flex items-center justify-center bg-gray-100 border border-gray-300 rounded text-gray-600 hover:bg-gray-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                                      type="button"
                                      title="Zvýšit množství"
                                    >
                                      <Plus className="h-3 w-3" />
                                    </button>
                                    {lot.isNew && (
                                      <button
                                        onClick={() => removeLot(index)}
                                        className="w-8 h-8 flex items-center justify-center text-red-600 hover:text-red-800 hover:bg-red-50 rounded focus:outline-none focus:ring-2 focus:ring-red-500 flex-shrink-0"
                                        type="button"
                                        title="Odstranit sarži"
                                      >
                                        <Trash2 className="h-3 w-3" />
                                      </button>
                                    )}
                                  </div>
                                </div>
                              ))}

                              {/* Add New Lot Button */}
                              <div className="mt-4 pt-4 border-t border-gray-200">
                                <button
                                  onClick={addNewLot}
                                  className="w-full flex items-center justify-center space-x-2 py-2 px-4 border-2 border-dashed border-gray-300 rounded-lg text-gray-600 hover:border-indigo-300 hover:text-indigo-600 transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500"
                                  type="button"
                                >
                                  <Plus className="h-4 w-4" />
                                  <span className="text-sm font-medium">Přidat novou sarži</span>
                                </button>
                              </div>

                              {/* Total Amount */}
                              <div className="mt-4 p-3 bg-indigo-50 rounded-lg border border-indigo-200">
                                <div className="flex justify-between items-center">
                                  <div className="text-sm font-medium text-indigo-700">
                                    Celkové množství:
                                  </div>
                                  <div className="text-lg font-bold text-indigo-800">
                                    {totalLotAmount.toFixed(2)}
                                  </div>
                                </div>
                              </div>
                            </div>
                          ) : (
                            <div className="h-full flex flex-col">
                              <div className="flex-1 flex items-center justify-center">
                                <div className="text-center text-gray-500">
                                  <Wrench className="h-8 w-8 mx-auto mb-2 text-gray-400" />
                                  <p className="text-sm">Žádné sarže</p>
                                  <p className="text-xs">nejsou k dispozici</p>
                                </div>
                              </div>
                              {/* Add New Lot Button */}
                              <div className="p-4 border-t bg-gray-50">
                                <button
                                  onClick={addNewLot}
                                  className="w-full flex items-center justify-center space-x-2 py-2 px-4 border-2 border-dashed border-gray-300 rounded-lg text-gray-600 hover:border-indigo-300 hover:text-indigo-600 transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500"
                                  type="button"
                                >
                                  <Plus className="h-4 w-4" />
                                  <span className="text-sm font-medium">Přidat novou sarži</span>
                                </button>
                              </div>
                            </div>
                          )}
                        </div>
                      </div>
                    ) : (
                      /* Product Image for non-lot materials */
                      <div className="w-full">
                        <div className="w-full bg-gray-200 rounded-lg overflow-hidden flex items-center justify-center min-h-[24rem]">
                          {effectiveItem?.image ? (
                            <img 
                              src={effectiveItem.image} 
                              alt={effectiveItem.productName || "Obrázek materiálu"}
                              className="max-w-full max-h-full object-contain"
                              onError={(e) => {
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
                              <Wrench className="h-12 w-12 mx-auto mb-2 text-gray-400" />
                              <p className="text-sm">Obrázek materiálu</p>
                              <p className="text-xs">není dostupný</p>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}
                  </div>
                </div>

                {/* Right Section - Product Info & Quantity Management (1/3 for lots) */}
                {effectiveItem?.hasLots ? (
                  <div className="w-full lg:w-1/3 p-6 bg-white border-l border-gray-200">
                    <div className="space-y-6">
                      {/* Product Info */}
                      <div className="space-y-3 pb-4 border-b border-gray-200">
                        {/* Product Code */}
                        <div className="flex justify-between items-center">
                          <span className="text-sm font-medium text-gray-600">
                            Kód materiálu:
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
                          Celkové množství ze sarží
                        </div>
                        <div className="text-4xl font-bold text-green-600 mb-2">
                          {totalLotAmount.toFixed(2)}
                        </div>
                        <div className="text-xs text-gray-500">
                          (ERP sklad: {currentStock})
                        </div>
                      </div>

                      {/* Lot-based instruction */}
                      <div className="text-center p-4 bg-blue-50 rounded-lg border border-blue-200">
                        <div className="text-sm text-blue-800">
                          Pro inventarizaci materiálu se sarží upravte množství jednotlivých sarží vlevo.
                          Celkové množství se bude počítat automaticky.
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
                            <span>Zinventarizovat materiál</span>
                          )}
                        </button>
                      </div>

                      {/* Error Message */}
                      {submitStockTaking.error && (
                        <div className="mt-4 p-3 bg-red-50 rounded-lg border border-red-200 flex items-start space-x-2">
                          <AlertCircle className="h-5 w-5 text-red-600 mt-0.5 flex-shrink-0" />
                          <div>
                            <div className="text-sm font-medium text-red-800">
                              Chyba při inventarizaci materiálu
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
                            Inventarizace materiálu byla úspěšně dokončena
                          </div>
                        </div>
                      )}

                      {/* Difference Display */}
                      {editableLots.some(lot => lot.amount !== lot.originalAmount) && (
                        <div className="mt-4 p-3 bg-yellow-50 rounded-lg border border-yellow-200">
                          <div className="text-center">
                            <span className="text-sm font-medium text-yellow-800">
                              Celkový rozdíl: {totalLotAmount > (effectiveItem?.lots?.reduce((sum, lot) => sum + (lot.amount ?? 0), 0) || 0) ? "+" : ""}{(totalLotAmount - (effectiveItem?.lots?.reduce((sum, lot) => sum + (lot.amount ?? 0), 0) || 0)).toFixed(2)}
                            </span>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                ) : (
                  /* Right Section for non-lot materials - quantity management */
                  <div className="w-full p-6 bg-white">
                    <div className="max-w-md mx-auto space-y-6">
                      {/* Product Info */}
                      <div className="space-y-3 pb-4 border-b border-gray-200">
                        {/* Product Code */}
                        <div className="flex justify-between items-center">
                          <span className="text-sm font-medium text-gray-600">
                            Kód materiálu:
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
                          Aktuální množství na skladě (ERP)
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
                            onClick={() => {
                              const newValue = Math.max(0, newQuantity - 1);
                              setNewQuantity(Math.round(newValue * 100) / 100);
                            }}
                            className="w-32 h-12 flex items-center justify-center bg-white border border-gray-300 rounded-lg text-gray-600 hover:bg-gray-50 hover:text-gray-900 touch-manipulation text-xl font-semibold"
                            type="button"
                          >
                            <Minus className="h-5 w-5" />
                          </button>
                          <input
                            type="number"
                            min="0"
                            step="0.01"
                            value={newQuantity.toFixed(2)}
                            onChange={(e) => {
                              const value = parseFloat(e.target.value);
                              const roundedValue = Math.round(Math.max(0, isNaN(value) ? 0 : value) * 100) / 100;
                              setNewQuantity(roundedValue);
                            }}
                            className="flex-1 text-center border border-gray-300 rounded-lg px-4 py-3 text-xl font-bold focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent touch-manipulation"
                          />
                          <button
                            onClick={() => {
                              const newValue = newQuantity + 1;
                              setNewQuantity(Math.round(newValue * 100) / 100);
                            }}
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
                            <span>Zinventarizovat materiál</span>
                          )}
                        </button>
                      </div>

                      {/* Error Message */}
                      {submitStockTaking.error && (
                        <div className="mt-4 p-3 bg-red-50 rounded-lg border border-red-200 flex items-start space-x-2">
                          <AlertCircle className="h-5 w-5 text-red-600 mt-0.5 flex-shrink-0" />
                          <div>
                            <div className="text-sm font-medium text-red-800">
                              Chyba při inventarizaci materiálu
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
                            Inventarizace materiálu byla úspěšně dokončena
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
                )}
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
                      Historie inventur materiálu ({historyData.totalCount || 0})
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
                        Žádné záznamy inventur pro tento materiál
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

export default ManufactureInventoryModal;