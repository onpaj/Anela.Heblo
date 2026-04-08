import React, { useState, useEffect } from "react";
import { X, AlertCircle, Package, Loader2, AlertTriangle } from "lucide-react";
import {
  ConfirmProductCompletionRequest,
  ProductActualQuantityRequest,
  ResidueDistributionDto,
} from "../../api/generated/api-client";

interface ProductQuantityData {
  id: number;
  productCode: string;
  productName: string;
  plannedQuantity: number;
}

interface ConfirmProductCompletionModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (request: ConfirmProductCompletionRequest) => Promise<void>;
  orderId: number;
  products: ProductQuantityData[];
  isLoading?: boolean;
  distributionPreview?: ResidueDistributionDto;
  onConfirmDistribution: () => Promise<void>;
  onBackFromDistribution: () => void;
}

const ConfirmProductCompletionModal: React.FC<ConfirmProductCompletionModalProps> = ({
  isOpen,
  onClose,
  onSubmit,
  orderId,
  products,
  isLoading = false,
  distributionPreview,
  onConfirmDistribution,
  onBackFromDistribution,
}) => {
  const [actualQuantities, setActualQuantities] = useState<{ [key: number]: string }>({});
  const [error, setError] = useState<string>('');

  // Initialize with planned quantities when modal opens.
  // Intentionally omit `products` from deps — re-renders caused by optimistic
  // cache updates must not reset values the user has already entered.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (isOpen && products.length > 0) {
      const initialQuantities: { [key: number]: string } = {};
      products.forEach(product => {
        initialQuantities[product.id] = product.plannedQuantity.toString();
      });
      setActualQuantities(initialQuantities);
      setError('');
    }
  }, [isOpen]);

  const handleQuantityChange = (productId: number, value: string) => {
    setActualQuantities(prev => ({
      ...prev,
      [productId]: value
    }));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    // Validate all quantities
    const productRequests: ProductActualQuantityRequest[] = [];
    for (const product of products) {
      const quantityStr = actualQuantities[product.id];
      const quantity = parseFloat(quantityStr);

      if (quantityStr === undefined || quantityStr === '' || isNaN(quantity) || quantity < 0) {
        setError(`Zadejte platné množství (0 nebo více) pro ${product.productName}`);
        return;
      }

      productRequests.push(new ProductActualQuantityRequest({
        id: product.id,
        actualQuantity: quantity
      }));
    }

    try {
      const request = new ConfirmProductCompletionRequest({
        id: orderId,
        products: productRequests,
        changeReason: 'Potvrzeno dokončení výroby produktů'
      });

      await onSubmit(request);
    } catch (err) {
      setError('Chyba při potvrzení množství produktů. Zkuste to prosím znovu.');
      console.error('Error confirming product completion:', err);
    }
  };

  const handleClose = () => {
    setError('');
    onClose();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      handleClose();
    }
  };

  if (!isOpen || products.length === 0) return null;

  // Distribution preview view
  if (distributionPreview) {
    const diffPct = distributionPreview.differencePercentage ?? 0;
    const allowedPct = distributionPreview.allowedResiduePercentage ?? 0;
    const diffPctFormatted = diffPct.toFixed(2);

    return (
      <div
        className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-[60]"
        onClick={(e) => e.target === e.currentTarget && handleClose()}
        onKeyDown={handleKeyDown}
      >
        <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full mx-4 max-h-[90vh] overflow-hidden">
          {/* Header */}
          <div className="flex items-center justify-between p-4 border-b border-gray-200">
            <h2 className="text-lg font-semibold text-gray-900 flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-orange-500" />
              Potvrdit distribuci zbytku
            </h2>
            <button
              onClick={handleClose}
              className="text-gray-400 hover:text-gray-600 transition-colors"
              disabled={isLoading}
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          {/* Content */}
          <div className="p-4 space-y-4 overflow-y-auto max-h-[calc(90vh-120px)]">
            {/* Warning banner */}
            <div className="bg-orange-50 border border-orange-200 rounded-lg p-3">
              <div className="flex items-start gap-2">
                <AlertTriangle className="h-4 w-4 text-orange-600 flex-shrink-0 mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-orange-800">
                    Rozdíl: <span className="font-bold">{diffPctFormatted}%</span>
                  </p>
                  <p className="text-sm text-orange-700 mt-0.5">
                    Přesahuje povolený práh {allowedPct}%. Zkontrolujte upravenou distribuci před potvrzením.
                  </p>
                </div>
              </div>
            </div>

            {/* Distribution table */}
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm border border-gray-200 rounded-lg overflow-hidden">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Produkt</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Kusů</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Teoreticky (g)</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Upraveno (g)</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">g/ks (stará BoM)</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">g/ks (nová BoM)</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {(distributionPreview.products ?? []).map((item, idx) => {
                    const theoretical = (item.actualPieces ?? 0) * (item.theoreticalGramsPerUnit ?? 0);
                    return (
                      <tr key={idx} className="hover:bg-gray-50">
                        <td className="px-3 py-2 text-gray-900 font-medium">
                          {item.productName}
                          <span className="ml-1 text-xs text-gray-400">{item.productCode}</span>
                        </td>
                        <td className="px-3 py-2 text-right text-gray-700">{item.actualPieces}</td>
                        <td className="px-3 py-2 text-right text-gray-700">{theoretical.toFixed(1)}</td>
                        <td className="px-3 py-2 text-right font-medium text-orange-700">
                          {(item.adjustedConsumption ?? 0).toFixed(1)}
                        </td>
                        <td className="px-3 py-2 text-right text-gray-500">
                          {(item.theoreticalGramsPerUnit ?? 0).toFixed(4)}
                        </td>
                        <td className="px-3 py-2 text-right text-gray-700 font-medium">
                          {(item.adjustedGramsPerUnit ?? 0).toFixed(4)}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* Buttons */}
            <div className="flex gap-3 pt-2">
              <button
                type="button"
                onClick={onBackFromDistribution}
                className="flex-1 px-3 py-2 text-gray-700 bg-gray-100 rounded hover:bg-gray-200 transition-colors text-sm"
                disabled={isLoading}
              >
                Zpět
              </button>
              <button
                type="button"
                onClick={onConfirmDistribution}
                className="flex-1 flex items-center justify-center gap-2 px-3 py-2 bg-orange-600 text-white rounded hover:bg-orange-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed text-sm"
                disabled={isLoading}
              >
                {isLoading ? (
                  <>
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Potvrzuji...
                  </>
                ) : (
                  'Potvrdit distribuci'
                )}
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Normal quantity input view
  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-[60]"
      onClick={(e) => e.target === e.currentTarget && handleClose()}
      onKeyDown={handleKeyDown}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full mx-4 max-h-[90vh] overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900 flex items-center gap-2">
            <Package className="h-4 w-4 text-indigo-600" />
            Potvrdit dokončení výroby
          </h2>
          <button
            onClick={handleClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
            disabled={isLoading}
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Content */}
        <form onSubmit={handleSubmit} className="p-4 space-y-3 overflow-y-auto max-h-[calc(90vh-120px)]">
          {/* Info */}
          <div className="bg-blue-50 rounded-lg p-3">
            <h3 className="font-medium text-gray-900 mb-1">Dokončení výroby produktů</h3>
            <p className="text-sm text-gray-600">
              Potvrďte skutečné množství vyrobených produktů
            </p>
          </div>

          {/* Products */}
          <div className="space-y-2">
            {products.map((product) => (
              <div key={product.id} className="border border-gray-200 rounded-lg p-3">
                <div className="flex items-center justify-between gap-4">
                  <div className="flex-1 min-w-0">
                    <h4 className="font-medium text-gray-900 truncate text-sm">{product.productName}</h4>
                    <div className="flex items-center gap-3 mt-1">
                      <p className="text-xs text-gray-600">{product.productCode}</p>
                      <p className="text-xs text-gray-500">
                        Plánované: <span className="font-medium">{product.plannedQuantity}</span>
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 flex-shrink-0">
                    <label
                      htmlFor={`quantity-${product.id}`}
                      className="text-sm font-medium text-gray-700 whitespace-nowrap"
                    >
                      Skutečné <span className="text-red-500">*</span>
                    </label>
                    <input
                      type="number"
                      id={`quantity-${product.id}`}
                      value={actualQuantities[product.id] || ''}
                      onChange={(e) => handleQuantityChange(product.id, e.target.value)}
                      className="w-20 px-2 py-1.5 border border-gray-300 rounded focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 text-center font-semibold text-sm"
                      min="0"
                      step="1"
                      disabled={isLoading}
                      required
                    />
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* Error Message */}
          {error && (
            <div className="flex items-center gap-2 p-2 bg-red-50 border border-red-200 rounded">
              <AlertCircle className="h-4 w-4 text-red-600 flex-shrink-0" />
              <span className="text-xs text-red-600">{error}</span>
            </div>
          )}

          {/* Buttons */}
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={handleClose}
              className="flex-1 px-3 py-2 text-gray-700 bg-gray-100 rounded hover:bg-gray-200 transition-colors text-sm"
              disabled={isLoading}
            >
              Zrušit
            </button>
            <button
              type="submit"
              className="flex-1 flex items-center justify-center gap-2 px-3 py-2 bg-indigo-600 text-white rounded hover:bg-indigo-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed text-sm"
              disabled={isLoading}
            >
              {isLoading ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Potvrzuji...
                </>
              ) : (
                'Potvrdit výrobu'
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default ConfirmProductCompletionModal;
