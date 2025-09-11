import React, { useState, useMemo } from "react";
import {
  RefreshCw,
  Package,
  X,
  AlertTriangle,
  CheckCircle,
} from "lucide-react";
import { useGiftPackageDetail } from "../../../api/hooks/useGiftPackageManufacturing";
import { GiftPackage } from "./GiftPackageManufacturingList";

interface GiftPackageManufacturingDetailProps {
  selectedPackage: GiftPackage | null;
  isOpen: boolean;
  onClose: () => void;
  onManufacture: (quantity: number) => Promise<void>;
}

const GiftPackageManufacturingDetail: React.FC<GiftPackageManufacturingDetailProps> = ({
  selectedPackage,
  isOpen,
  onClose,
  onManufacture,
}) => {
  const [quantity, setQuantity] = useState(1);

  // Load gift package detail with components when modal is open
  const { data: giftPackageDetail, isLoading: detailLoading } = useGiftPackageDetail(
    selectedPackage?.code
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
      setQuantity(Math.max(1, selectedPackage.suggestedQuantity));
    }
  }, [selectedPackage]);

  const handleManufacture = async () => {
    if (!selectedPackage || !validationResults.isValid) return;
    
    try {
      await onManufacture(quantity);
      onClose();
    } catch (error) {
      console.error('Manufacturing error:', error);
    }
  };

  if (!isOpen || !selectedPackage) {
    return null;
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-2 sm:p-4">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-[98vw] xl:max-w-[1600px] h-[95vh] sm:h-[85vh] flex flex-col">
        {/* Header - Compact */}
        <div className="flex items-center justify-between p-3 sm:p-4 border-b border-gray-200 bg-gray-50 rounded-t-lg flex-shrink-0">
          <div className="flex-1 min-w-0">
            <h2 className="text-base sm:text-lg font-semibold text-gray-900 truncate">
              {selectedPackage.name}
            </h2>
            <p className="text-xs text-gray-600">
              Kód: {selectedPackage.code}
            </p>
          </div>
          <button
            onClick={onClose}
            className="ml-4 flex-shrink-0 p-2 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded-full transition-colors touch-manipulation"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Main Content - Horizontal Layout */}
        <div className="flex-1 flex flex-col lg:flex-row min-h-0">
          {/* Left Panel - Components List */}
          <div className="flex-1 lg:flex-none lg:w-3/4 border-b lg:border-b-0 lg:border-r border-gray-200 flex flex-col min-h-0">
            <div className="p-3 sm:p-4 bg-gray-50 border-b border-gray-200">
              <h3 className="text-sm sm:text-base font-medium text-gray-900 flex items-center">
                <Package className="h-4 w-4 mr-2" />
                Složení balíčku
                {giftPackageDetail?.giftPackage?.ingredients && (
                  <span className="ml-2 text-xs text-gray-600">
                    ({giftPackageDetail.giftPackage.ingredients.length} položek)
                  </span>
                )}
              </h3>
            </div>
            
            <div className="flex-1 overflow-auto">
              {detailLoading ? (
                <div className="flex items-center justify-center py-8">
                  <RefreshCw className="h-6 w-6 animate-spin text-gray-400 mr-2" />
                  <span className="text-gray-600">Načítání komponent...</span>
                </div>
              ) : validationResults.insufficientIngredients.length > 0 ? (
                <div className="bg-white">
                  {/* Grid Header */}
                  <div className="grid gap-2 px-3 py-2 bg-gray-50 border-b border-gray-200 text-xs font-medium text-gray-500 uppercase tracking-wider" style={{gridTemplateColumns: '1fr 80px 80px 80px 80px'}}>
                    <div className="">Komponenta</div>
                    <div className="text-center">Na kus</div>
                    <div className="text-center">Celkem ({quantity}x)</div>
                    <div className="text-center">Skladem</div>
                    <div className="text-center">Stav</div>
                  </div>
                  
                  {/* Grid Body */}
                  <div className="divide-y divide-gray-100">
                    {validationResults.insufficientIngredients.map((ingredient, index) => (
                      <div key={index}>
                        {/* Main Row */}
                        <div className={`grid gap-2 px-3 py-3 hover:bg-gray-50 transition-colors ${ingredient.isInsufficient ? 'bg-red-50/30' : ''}`} style={{gridTemplateColumns: '1fr 80px 80px 80px 80px'}}>
                          {/* Product Info */}
                          <div className="min-w-0">
                            <div className="text-sm font-medium text-gray-900 truncate">
                              {ingredient.productName}
                            </div>
                            <div className="text-xs text-gray-500 mt-1">
                              {ingredient.productCode}
                              {(ingredient as any).location && (
                                <span className="ml-2 text-gray-400">
                                  • {(ingredient as any).location}
                                </span>
                              )}
                            </div>
                          </div>
                          
                          {/* Quantity per unit */}
                          <div className="text-sm text-gray-900 flex items-center justify-center">
                            {(ingredient.requiredQuantity || 0).toFixed(1)}
                          </div>
                          
                          {/* Total required */}
                          <div className="text-sm font-medium text-gray-900 flex items-center justify-center">
                            {ingredient.requiredTotal.toFixed(1)}
                          </div>
                          
                          {/* Available stock */}
                          <div className={`text-sm font-medium flex items-center justify-center ${ingredient.isInsufficient ? 'text-red-600' : 'text-gray-900'}`}>
                            {ingredient.availableStock.toFixed(1)}
                          </div>
                          
                          {/* Status */}
                          <div className="flex items-center justify-center">
                            {ingredient.isInsufficient ? (
                              <span className="text-red-500 text-lg" title="Nedostatečné zásoby">⚠</span>
                            ) : (
                              <span className="text-green-500 text-lg" title="Dostatečné zásoby">✓</span>
                            )}
                          </div>
                        </div>
                        
                        {/* Sub-row for shortage info */}
                        {ingredient.isInsufficient && (
                          <div className="flex px-3 py-2 bg-red-50/50 border-l-4 border-red-200">
                            <div className="flex-1 text-xs text-red-600">
                              ⚠ Nedostatečné zásoby
                            </div>
                            <div className="text-xs text-red-600">
                              Chybí: {ingredient.shortage.toFixed(1)} ks
                            </div>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="flex items-center justify-center py-8 text-gray-500">
                  <Package className="h-8 w-8 mr-3" />
                  <span>Komponenty nejsou k dispozici</span>
                </div>
              )}
            </div>
          </div>

          {/* Right Panel - Statistics & Controls */}
          <div className="flex-shrink-0 lg:w-1/4 flex flex-col">
            {/* Statistics */}
            <div className="p-3 sm:p-4 bg-gray-50 border-b border-gray-200">
              <h3 className="text-sm sm:text-base font-medium text-gray-900 mb-3">
                Statistiky balíčku
              </h3>
              <div className="grid grid-cols-2 gap-3">
                <div className="bg-white rounded-lg p-3 text-center border">
                  <div className="text-xs text-gray-500 mb-1">Aktuální sklad</div>
                  <div className="text-lg font-bold text-gray-900">{selectedPackage.availableStock.toFixed(0)}</div>
                  <div className="text-xs text-gray-500">ks</div>
                </div>
                <div className="bg-white rounded-lg p-3 text-center border">
                  <div className="text-xs text-gray-500 mb-1">Prodeje/den</div>
                  <div className="text-lg font-bold text-gray-900">{selectedPackage.dailySales.toFixed(1)}</div>
                  <div className="text-xs text-gray-500">ks</div>
                </div>
                <div className="bg-white rounded-lg p-3 text-center border">
                  <div className="text-xs text-gray-500 mb-1">Doporučeno</div>
                  <div className="text-lg font-bold text-orange-600">{selectedPackage.suggestedQuantity}</div>
                  <div className="text-xs text-gray-500">ks</div>
                </div>
                <div className="bg-white rounded-lg p-3 text-center border">
                  <div className="text-xs text-gray-500 mb-1">Týdenní spotřeba</div>
                  <div className="text-lg font-bold text-gray-900">{(selectedPackage.dailySales * 7).toFixed(1)}</div>
                  <div className="text-xs text-gray-500">ks</div>
                </div>
              </div>
            </div>

            {/* Manufacturing Controls */}
            <div className="flex-1 p-3 sm:p-4 bg-indigo-50">
              <h3 className="text-sm sm:text-base font-medium text-gray-900 mb-4 flex items-center">
                <Package className="h-4 w-4 mr-2" />
                Výrobní příkaz
              </h3>
              
              {/* Quantity Input - Touch Friendly */}
              <div className="mb-4">
                <label className="block text-sm font-medium text-gray-700 mb-2">
                  Množství k výrobě (ks)
                </label>
                <div className="flex items-center space-x-3 mb-3">
                  <button
                    onClick={() => setQuantity(Math.max(1, quantity - 1))}
                    className="w-32 h-12 flex items-center justify-center bg-white border border-gray-300 rounded-lg text-gray-600 hover:bg-gray-50 hover:text-gray-900 touch-manipulation text-xl font-semibold"
                    type="button"
                  >
                    -
                  </button>
                  <input
                    type="number"
                    min="1"
                    value={quantity}
                    onChange={(e) => setQuantity(Math.max(1, parseInt(e.target.value) || 1))}
                    className="flex-1 text-center border border-gray-300 rounded-lg px-4 py-3 text-xl font-bold focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent touch-manipulation"
                  />
                  <button
                    onClick={() => setQuantity(quantity + 1)}
                    className="w-32 h-12 flex items-center justify-center bg-white border border-gray-300 rounded-lg text-gray-600 hover:bg-gray-50 hover:text-gray-900 touch-manipulation text-xl font-semibold"
                    type="button"
                  >
                    +
                  </button>
                </div>
                
                {/* Quick buttons */}
                <div className="grid grid-cols-2 gap-2 mb-4">
                  <button
                    onClick={() => setQuantity(selectedPackage.suggestedQuantity)}
                    className="px-3 py-2 text-sm bg-orange-100 text-orange-700 rounded-lg hover:bg-orange-200 transition-colors touch-manipulation text-center"
                    type="button"
                  >
                    Doporučeno<br/>({selectedPackage.suggestedQuantity})
                  </button>
                  <button
                    onClick={() => setQuantity(Math.ceil(selectedPackage.dailySales * 7))}
                    className="px-3 py-2 text-sm bg-blue-100 text-blue-700 rounded-lg hover:bg-blue-200 transition-colors touch-manipulation text-center"
                    type="button"
                  >
                    Týdenní<br/>({Math.ceil(selectedPackage.dailySales * 7)})
                  </button>
                </div>
              </div>

              {/* Validation Status */}
              {validationResults.insufficientIngredients.length > 0 && (
                <div className={`p-3 rounded-lg mb-4 ${validationResults.isValid ? 'bg-green-100 border border-green-200' : 'bg-red-100 border border-red-200'}`}>
                  <div className="flex items-center">
                    {validationResults.isValid ? (
                      <>
                        <CheckCircle className="h-4 w-4 text-green-600 mr-2" />
                        <span className="text-sm font-medium text-green-800">
                          Všechny komponenty jsou dostupné
                        </span>
                      </>
                    ) : (
                      <>
                        <AlertTriangle className="h-4 w-4 text-red-600 mr-2" />
                        <span className="text-sm font-medium text-red-800">
                          Nedostatečné zásoby ({validationResults.insufficientIngredients.filter(ing => ing.isInsufficient).length} položek)
                        </span>
                      </>
                    )}
                  </div>
                </div>
              )}

              {/* Manufacturing Button - Touch Friendly */}
              <button
                onClick={handleManufacture}
                disabled={!validationResults.isValid}
                className={`w-full flex items-center justify-center px-6 py-4 text-lg font-semibold rounded-lg transition-colors touch-manipulation ${
                  validationResults.isValid
                    ? 'text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500'
                    : 'text-gray-400 bg-gray-200 cursor-not-allowed'
                }`}
              >
                <Package className="h-5 w-5 mr-2" />
                Vyrobit ({quantity} ks)
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default GiftPackageManufacturingDetail;