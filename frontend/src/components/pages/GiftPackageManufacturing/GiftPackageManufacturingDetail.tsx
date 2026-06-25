import React, { useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import {
  RefreshCw,
  Package,
  PackageOpen,
  X,
  AlertTriangle,
  CheckCircle,
  FileText,
} from "lucide-react";
import { useGiftPackageDetail, useDisassembleGiftPackage } from "../../../api/hooks/useGiftPackageManufacturing";
import { GiftPackage } from "./GiftPackageManufacturingList";
import { toast } from "react-hot-toast";
import DisassemblyTabContent from "./DisassemblyTabContent";

interface GiftPackageManufacturingDetailProps {
  selectedPackage: GiftPackage | null;
  isOpen: boolean;
  onClose: () => void;
  onManufacture: (quantity: number) => Promise<void>;
  onEnqueueManufacture: (quantity: number) => Promise<void>;
  salesCoefficient?: number;
  fromDate?: Date;
  toDate?: Date;
}

const GiftPackageManufacturingDetail: React.FC<GiftPackageManufacturingDetailProps> = ({
  selectedPackage,
  isOpen,
  onClose,
  onManufacture,
  onEnqueueManufacture,
  salesCoefficient,
  fromDate,
  toDate,
}) => {
  const navigate = useNavigate();
  const [quantity, setQuantity] = useState(1);
  const [disassemblyQuantity, setDisassemblyQuantity] = useState(1);
  const [activeTab, setActiveTab] = useState<'manufacture' | 'disassemble'>('manufacture');

  // Disassembly mutation
  const disassemblyMutation = useDisassembleGiftPackage();

  // Load gift package detail with components when modal is open
  const { data: giftPackageDetail, isLoading: detailLoading } = useGiftPackageDetail(
    selectedPackage?.code,
    salesCoefficient,
    fromDate,
    toDate
  );

  // Calculate validation results
  const validationResults = useMemo(() => {
    if (!giftPackageDetail?.giftPackage?.ingredients || quantity <= 0) {
      return { isValid: false, insufficientIngredients: [], hasAnyStock: false };
    }

    const insufficientIngredients = giftPackageDetail.giftPackage.ingredients
      .map(ingredient => {
        const requiredTotal = (ingredient.requiredQuantity || 0) * quantity;
        const availableStock = ingredient.availableStock || 0;
        const isInsufficient = availableStock < requiredTotal;
        
        return {
          ...ingredient,
          requiredTotal,
          availableStock,
          isInsufficient,
          shortage: isInsufficient ? requiredTotal - availableStock : 0
        };
      });

    const hasInsufficientStock = insufficientIngredients.some(ing => ing.isInsufficient);
    const hasAnyStock = insufficientIngredients.some(ing => ing.availableStock > 0);

    return {
      isValid: !hasInsufficientStock,
      insufficientIngredients,
      hasAnyStock
    };
  }, [giftPackageDetail?.giftPackage?.ingredients, quantity]);

  // Reset quantity when package changes
  React.useEffect(() => {
    if (selectedPackage) {
      // Default quantity = max(0, SuggestedQuantity - AvailableStock), minimum 1
      const neededQuantity = Math.max(0, selectedPackage.suggestedQuantity - selectedPackage.availableStock);
      setQuantity(Math.max(1, neededQuantity));
      // Default disassembly quantity to 1
      setDisassemblyQuantity(1);
      // Reset to manufacture tab
      setActiveTab('manufacture');
    }
  }, [selectedPackage]);


  const handleEnqueueManufacture = async () => {
    if (!selectedPackage) return;

    try {
      await onEnqueueManufacture(quantity);
      onClose();
    } catch (error) {
      console.error('Enqueue manufacturing error:', error);
    }
  };

  const handleDisassemble = async () => {
    if (!selectedPackage) return;

    try {
      const request = {
        giftPackageCode: selectedPackage.code,
        quantity: disassemblyQuantity,
      };

      const response = await disassemblyMutation.mutateAsync(request as any);

      if (response.success) {
        toast.success(`Balíček ${selectedPackage.code} byl úspěšně rozebrán (${disassemblyQuantity} ks)`);
        onClose();
      } else {
        const errorMessage = response.params?.['ErrorMessage'] || 'Nepodařilo se rozebrat balíček';
        toast.error(errorMessage);
      }
    } catch (error: any) {
      console.error('Disassembly error:', error);
      toast.error(error?.message || 'Chyba při rozebírání balíčku');
    }
  };

  // Handle navigation to StockUpOperations filtered by this gift package product
  const handleViewStockUpOperations = () => {
    if (selectedPackage?.code) {
      navigate(`/stock-up-operations?sourceType=GiftPackageManufacture&productCode=${selectedPackage.code}&state=Active`);
      onClose(); // Close modal after navigation
    }
  };

  if (!isOpen || !selectedPackage) {
    return null;
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-2 sm:p-4">
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark w-full max-w-[98vw] xl:max-w-[1600px] h-[95vh] sm:h-[85vh] flex flex-col">
        {/* Header - Compact */}
        <div className="flex items-center justify-between p-3 sm:p-4 border-b border-gray-200 dark:border-graphite-border bg-gray-50 dark:bg-graphite-surface-2 rounded-t-lg flex-shrink-0">
          <div className="flex-1 min-w-0">
            <h2 className="text-base sm:text-lg font-semibold text-gray-900 dark:text-graphite-text truncate">
              {selectedPackage.name}
            </h2>
            <p className="text-xs text-gray-600 dark:text-graphite-muted">
              Kód: {selectedPackage.code}
            </p>
          </div>
          <button
            onClick={onClose}
            className="ml-4 flex-shrink-0 p-2 text-gray-400 dark:text-graphite-faint hover:text-gray-600 hover:bg-gray-100 dark:hover:bg-white/5 rounded-full transition-colors touch-manipulation"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Main Content - Horizontal Layout */}
        <div className="flex-1 flex flex-col lg:flex-row min-h-0">
          {/* Left Panel - Components List */}
          <div className="flex-1 lg:flex-none lg:w-3/4 border-b lg:border-b-0 lg:border-r border-gray-200 dark:border-graphite-border flex flex-col min-h-0">
            <div className="p-3 sm:p-4 bg-gray-50 dark:bg-graphite-surface-2 border-b border-gray-200 dark:border-graphite-border">
              <h3 className="text-sm sm:text-base font-medium text-gray-900 dark:text-graphite-text flex items-center">
                <Package className="h-4 w-4 mr-2" />
                Složení balíčku
                {giftPackageDetail?.giftPackage?.ingredients && (
                  <span className="ml-2 text-xs text-gray-600 dark:text-graphite-muted">
                    ({giftPackageDetail.giftPackage.ingredients.length} položek)
                  </span>
                )}
              </h3>
            </div>
            
            <div className="flex-1 overflow-auto">
              {detailLoading ? (
                <div className="flex items-center justify-center py-8">
                  <RefreshCw className="h-6 w-6 animate-spin text-gray-400 dark:text-graphite-faint mr-2" />
                  <span className="text-gray-600 dark:text-graphite-muted">Načítání komponent...</span>
                </div>
              ) : validationResults.insufficientIngredients.length > 0 ? (
                <div className="bg-white dark:bg-graphite-surface">
                  {/* Grid Header */}
                  <div className="grid gap-2 px-3 py-2 bg-gray-50 dark:bg-graphite-surface-2 border-b border-gray-200 dark:border-graphite-border text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider" style={{gridTemplateColumns: '1fr 80px 80px 80px 80px'}}>
                    <div className="">Komponenta</div>
                    <div className="text-center">Na kus</div>
                    <div className="text-center">Celkem ({quantity}x)</div>
                    <div className="text-center">Skladem</div>
                    <div className="text-center">Stav</div>
                  </div>
                  
                  {/* Grid Body */}
                  <div className="divide-y divide-gray-100 dark:divide-graphite-border">
                    {validationResults.insufficientIngredients.map((ingredient, index) => (
                      <div key={index}>
                        {/* Main Row */}
                        <div className={`grid gap-2 px-3 py-3 hover:bg-gray-50 dark:hover:bg-white/5 transition-colors ${ingredient.isInsufficient ? 'bg-red-50/30 dark:bg-red-900/10' : ''}`} style={{gridTemplateColumns: '1fr 80px 80px 80px 80px'}}>
                          {/* Product Info */}
                          <div className="min-w-0">
                            <div className="text-sm font-medium text-gray-900 dark:text-graphite-text truncate">
                              {ingredient.productName}
                            </div>
                            <div className="text-xs text-gray-500 dark:text-graphite-muted mt-1">
                              {ingredient.productCode}
                              {(ingredient as any).location && (
                                <span className="ml-2 text-gray-400 dark:text-graphite-faint">
                                  • {(ingredient as any).location}
                                </span>
                              )}
                            </div>
                          </div>
                          
                          {/* Quantity per unit */}
                          <div className="text-sm text-gray-900 dark:text-graphite-text flex items-center justify-center">
                            {(ingredient.requiredQuantity || 0).toFixed(1)}
                          </div>

                          {/* Total required */}
                          <div className="text-sm font-medium text-gray-900 dark:text-graphite-text flex items-center justify-center">
                            {ingredient.requiredTotal.toFixed(1)}
                          </div>

                          {/* Available stock */}
                          <div className={`text-sm font-medium flex items-center justify-center ${ingredient.isInsufficient ? 'text-red-600 dark:text-red-400' : 'text-gray-900 dark:text-graphite-text'}`}>
                            {ingredient.availableStock.toFixed(1)}
                          </div>

                          {/* Status */}
                          <div className="flex items-center justify-center">
                            {ingredient.isInsufficient ? (
                              <span className="text-red-500 dark:text-red-400 text-lg" title="Nedostatečné zásoby">⚠</span>
                            ) : (
                              <span className="text-green-500 dark:text-emerald-400 text-lg" title="Dostatečné zásoby">✓</span>
                            )}
                          </div>
                        </div>

                        {/* Sub-row for shortage info */}
                        {ingredient.isInsufficient && (
                          <div className="flex px-3 py-2 bg-red-50/50 dark:bg-red-900/10 border-l-4 border-red-200 dark:border-graphite-border">
                            <div className="flex-1 text-xs text-red-600 dark:text-red-400">
                              ⚠ Nedostatečné zásoby
                            </div>
                            <div className="text-xs text-red-600 dark:text-red-400">
                              Chybí: {ingredient.shortage.toFixed(1)} ks
                            </div>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="flex items-center justify-center py-8 text-gray-500 dark:text-graphite-muted">
                  <Package className="h-8 w-8 mr-3" />
                  <span>Komponenty nejsou k dispozici</span>
                </div>
              )}
            </div>
          </div>

          {/* Right Panel - Statistics & Controls */}
          <div className="flex-shrink-0 lg:w-1/4 flex flex-col">
            {/* Statistics */}
            <div className="p-3 sm:p-4 bg-gray-50 dark:bg-graphite-surface-2 border-b border-gray-200 dark:border-graphite-border">
              <h3 className="text-sm sm:text-base font-medium text-gray-900 dark:text-graphite-text mb-3">
                Statistiky balíčku
              </h3>
              <div className="grid grid-cols-2 gap-2">
                <div className="bg-white dark:bg-graphite-surface rounded-md p-2 text-center border dark:border-graphite-border">
                  <div className="text-xs text-gray-500 dark:text-graphite-muted">Aktuální sklad</div>
                  <div className="text-base font-bold text-gray-900 dark:text-graphite-text">{selectedPackage.availableStock.toFixed(0)}</div>
                  <div className="text-xs text-gray-500 dark:text-graphite-muted">ks</div>
                </div>
                <div className="bg-white dark:bg-graphite-surface rounded-md p-2 text-center border dark:border-graphite-border">
                  <div className="text-xs text-gray-500 dark:text-graphite-muted">Doporučeno</div>
                  <div className="text-base font-bold text-orange-600 dark:text-orange-400">{selectedPackage.suggestedQuantity}</div>
                  <div className="text-xs text-gray-500 dark:text-graphite-muted">ks</div>
                </div>
              </div>
            </div>

            {/* Tab Headers */}
            <div className="flex border-b border-gray-200 dark:border-graphite-border">
              <button
                onClick={() => setActiveTab('manufacture')}
                className={`flex-1 px-4 py-3 text-sm font-medium transition-colors ${
                  activeTab === 'manufacture'
                    ? 'text-indigo-700 dark:text-graphite-accent bg-indigo-50 dark:bg-graphite-accent/10 border-b-2 border-indigo-600 dark:border-graphite-accent'
                    : 'text-gray-500 dark:text-graphite-muted hover:text-gray-700 hover:bg-gray-50 dark:hover:bg-white/5'
                }`}
              >
                <Package className="h-4 w-4 inline-block mr-2" />
                Výroba
              </button>
              <button
                onClick={() => setActiveTab('disassemble')}
                className={`flex-1 px-4 py-3 text-sm font-medium transition-colors ${
                  activeTab === 'disassemble'
                    ? 'text-red-700 dark:text-red-300 bg-red-50 dark:bg-red-900/30 border-b-2 border-red-600 dark:border-red-500'
                    : 'text-gray-500 dark:text-graphite-muted hover:text-gray-700 hover:bg-gray-50 dark:hover:bg-white/5'
                }`}
              >
                <PackageOpen className="h-4 w-4 inline-block mr-2" />
                Rozebírání
              </button>
            </div>

            {/* Manufacturing Controls - Shown when activeTab === 'manufacture' */}
            {activeTab === 'manufacture' && (
              <div className="flex-1 p-3 sm:p-4 bg-indigo-50 dark:bg-graphite-accent/10">
              <h3 className="text-sm sm:text-base font-medium text-gray-900 dark:text-graphite-text mb-4 flex items-center">
                <Package className="h-4 w-4 mr-2" />
                Výrobní příkaz
              </h3>

              {/* Quantity Input - Touch Friendly */}
              <div className="mb-4">
                <label className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2">
                  Množství k výrobě (ks)
                </label>
                <div className="flex items-center space-x-2 mb-3">
                  <button
                    onClick={() => setQuantity(Math.max(1, quantity - 1))}
                    className="w-16 h-16 flex items-center justify-center bg-white dark:bg-graphite-surface border-2 border-gray-300 dark:border-graphite-border rounded-xl text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 hover:text-gray-900 dark:hover:text-graphite-text hover:border-gray-400 active:bg-gray-100 touch-manipulation text-3xl font-bold transition-all duration-150 shadow-sm dark:shadow-soft-dark"
                    type="button"
                  >
                    -
                  </button>
                  <input
                    type="number"
                    min="1"
                    value={quantity}
                    onChange={(e) => setQuantity(Math.max(1, parseInt(e.target.value) || 1))}
                    className="flex-1 text-center border-2 border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-xl px-3 py-4 text-xl font-bold focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 touch-manipulation shadow-sm dark:shadow-soft-dark min-w-0"
                  />
                  <button
                    onClick={() => setQuantity(quantity + 1)}
                    className="w-16 h-16 flex items-center justify-center bg-white dark:bg-graphite-surface border-2 border-gray-300 dark:border-graphite-border rounded-xl text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 hover:text-gray-900 dark:hover:text-graphite-text hover:border-gray-400 active:bg-gray-100 touch-manipulation text-3xl font-bold transition-all duration-150 shadow-sm dark:shadow-soft-dark"
                    type="button"
                  >
                    +
                  </button>
                </div>
                
                {/* Quick buttons */}
                <div className="grid grid-cols-2 gap-2 mb-4">
                  <button
                    onClick={() => {
                      const neededQuantity = Math.max(0, selectedPackage.suggestedQuantity - selectedPackage.availableStock);
                      setQuantity(Math.max(1, neededQuantity));
                    }}
                    className="px-3 py-2 text-sm bg-orange-100 dark:bg-orange-900/30 text-orange-700 dark:text-orange-300 rounded-lg hover:bg-orange-200 dark:hover:bg-orange-900/50 transition-colors touch-manipulation text-center"
                    type="button"
                  >
                    Potřebné<br/>({Math.max(1, Math.max(0, selectedPackage.suggestedQuantity - selectedPackage.availableStock))})
                  </button>
                  <button
                    onClick={() => setQuantity(Math.ceil(selectedPackage.dailySales * 7))}
                    className="px-3 py-2 text-sm bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 rounded-lg hover:bg-blue-200 dark:hover:bg-blue-900/50 transition-colors touch-manipulation text-center"
                    type="button"
                  >
                    Týdenní<br/>({Math.ceil(selectedPackage.dailySales * 7)})
                  </button>
                </div>
              </div>

              {/* Validation Status */}
              {validationResults.insufficientIngredients.length > 0 && (
                <div className={`p-3 rounded-lg mb-4 ${validationResults.isValid ? 'bg-green-100 dark:bg-emerald-900/30 border border-green-200 dark:border-graphite-border' : 'bg-red-100 dark:bg-red-900/30 border border-red-200 dark:border-graphite-border'}`}>
                  <div className="flex items-center">
                    {validationResults.isValid ? (
                      <>
                        <CheckCircle className="h-4 w-4 text-green-600 dark:text-emerald-400 mr-2" />
                        <span className="text-sm font-medium text-green-800 dark:text-emerald-300">
                          Všechny komponenty jsou dostupné
                        </span>
                      </>
                    ) : (
                      <>
                        <AlertTriangle className="h-4 w-4 text-red-600 dark:text-red-400 mr-2" />
                        <span className="text-sm font-medium text-red-800 dark:text-red-300">
                          Nedostatečné zásoby ({validationResults.insufficientIngredients.filter(ing => ing.isInsufficient).length} položek)
                        </span>
                      </>
                    )}
                  </div>
                </div>
              )}

              {/* Manufacturing Buttons - Touch Friendly */}
              <div className="space-y-3">
                {/* Asynchronous Manufacturing Button */}
                <button
                  onClick={handleEnqueueManufacture}
                  disabled={!validationResults.isValid}
                  className={`w-full flex items-center justify-center px-6 py-4 text-lg font-semibold rounded-lg transition-colors touch-manipulation ${
                    validationResults.isValid
                      ? 'text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500'
                      : 'text-gray-400 dark:text-graphite-faint bg-gray-200 dark:bg-graphite-hover cursor-not-allowed'
                  }`}
                >
                  <RefreshCw className="h-5 w-5 mr-2" />
                  Zadat k výrobě ({quantity} ks)
                </button>

                {/* View Stock-Up Operations Button */}
                <button
                  onClick={handleViewStockUpOperations}
                  disabled={!selectedPackage?.code}
                  className="w-full flex items-center justify-center px-4 py-3 text-sm font-medium text-indigo-600 dark:text-graphite-accent bg-white dark:bg-graphite-surface border-2 border-indigo-200 dark:border-graphite-border rounded-lg hover:bg-indigo-50 dark:hover:bg-graphite-accent/10 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed touch-manipulation"
                >
                  <FileText className="h-4 w-4 mr-2" />
                  Zobrazit operace naskladnění
                </button>
              </div>
            </div>
            )}

            {/* Disassembly Controls - Shown when activeTab === 'disassemble' */}
            {activeTab === 'disassemble' && (
              <DisassemblyTabContent
                selectedPackage={selectedPackage}
                quantity={disassemblyQuantity}
                setQuantity={setDisassemblyQuantity}
                maxQuantity={selectedPackage.availableStock}
                onDisassemble={handleDisassemble}
                isPending={disassemblyMutation.isPending}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default GiftPackageManufacturingDetail;