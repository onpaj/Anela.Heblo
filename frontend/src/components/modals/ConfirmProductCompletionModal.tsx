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
  semiProductCode?: string;
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
  semiProductCode,
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
  useEffect(() => {
    if (isOpen && products.length > 0) {
      const initialQuantities: { [key: number]: string } = {};
      products.forEach(product => {
        initialQuantities[product.id] = product.plannedQuantity.toString();
      });
      setActualQuantities(initialQuantities);
      setError('');
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
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
        <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-4xl w-full mx-4 max-h-[90vh] overflow-hidden">
          {/* Header */}
          <div className="flex items-center justify-between p-4 border-b border-gray-200 dark:border-graphite-border">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-graphite-text flex items-center gap-2">
              <AlertTriangle className="h-4 w-4 text-orange-500 dark:text-orange-300" />
              Potvrdit distribuci zbytku
            </h2>
            <button
              onClick={handleClose}
              className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted transition-colors"
              disabled={isLoading}
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          {/* Content */}
          <div className="p-4 space-y-4 overflow-y-auto max-h-[calc(90vh-120px)]">
            {/* Warning banner */}
            <div className="bg-orange-50 border border-orange-200 dark:bg-orange-900/20 dark:border-orange-900/40 rounded-lg p-3">
              <div className="flex items-start gap-2">
                <AlertTriangle className="h-4 w-4 text-orange-600 dark:text-orange-300 flex-shrink-0 mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-orange-800 dark:text-orange-300">
                    Rozdíl: <span className="font-bold">{diffPctFormatted}%</span>
                  </p>
                  <p className="text-sm text-orange-700 dark:text-orange-400 mt-0.5">
                    Přesahuje povolený práh {allowedPct}%. Zkontrolujte upravenou distribuci před potvrzením.
                  </p>
                </div>
              </div>
            </div>

            {/* Distribution table */}
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm border border-gray-200 dark:border-graphite-border rounded-lg overflow-hidden">
                <thead className="bg-gray-50 dark:bg-graphite-surface-2">
                  <tr>
                    <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Produkt</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Kusů</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Teoreticky (g)</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Upraveno (g)</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">g/ks (stará BoM)</th>
                    <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">g/ks (nová BoM)</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100 dark:divide-graphite-border">
                  {(distributionPreview.products ?? []).map((item, idx) => {
                    const theoretical = (item.actualPieces ?? 0) * (item.theoreticalGramsPerUnit ?? 0);
                    return (
                      <tr key={idx} className="hover:bg-gray-50 dark:hover:bg-white/5">
                        <td className="px-3 py-2 text-gray-900 dark:text-graphite-text font-medium">
                          {item.productName}
                          <span className="ml-1 text-xs text-gray-400 dark:text-graphite-faint">{item.productCode}</span>
                        </td>
                        <td className="px-3 py-2 text-right text-gray-700 dark:text-graphite-muted">{item.actualPieces}</td>
                        <td className="px-3 py-2 text-right text-gray-700 dark:text-graphite-muted">{theoretical.toFixed(1)}</td>
                        <td className="px-3 py-2 text-right font-medium text-orange-700 dark:text-orange-400">
                          {(item.adjustedConsumption ?? 0).toFixed(1)}
                        </td>
                        <td className="px-3 py-2 text-right text-gray-500 dark:text-graphite-muted">
                          {(item.theoreticalGramsPerUnit ?? 0).toFixed(4)}
                        </td>
                        <td className="px-3 py-2 text-right text-gray-700 dark:text-graphite-muted font-medium">
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
                className="flex-1 px-3 py-2 text-gray-700 bg-gray-100 rounded hover:bg-gray-200 dark:text-graphite-muted dark:bg-graphite-surface-2 dark:hover:bg-graphite-hover transition-colors text-sm"
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
      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-4xl w-full mx-4 max-h-[90vh] overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-gray-200 dark:border-graphite-border">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-graphite-text flex items-center gap-2">
            <Package className="h-4 w-4 text-indigo-600 dark:text-graphite-accent" />
            Potvrdit dokončení výroby
          </h2>
          <button
            onClick={handleClose}
            className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted transition-colors"
            disabled={isLoading}
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Content */}
        <form onSubmit={handleSubmit} className="p-4 space-y-3 overflow-y-auto max-h-[calc(90vh-120px)]">
          {/* Info */}
          <div className="bg-blue-50 dark:bg-blue-900/20 rounded-lg p-3">
            <h3 className="font-medium text-gray-900 dark:text-graphite-text mb-1">Dokončení výroby produktů</h3>
            <p className="text-sm text-gray-600 dark:text-graphite-muted">
              Potvrďte skutečné množství vyrobených produktů
            </p>
          </div>

          {/* Products */}
          <div className="space-y-2">
            {products.map((product) => {
              const isDirectRow = semiProductCode && product.productCode === semiProductCode;
              return (
                <div
                  key={product.id}
                  className={`border rounded-lg p-3 ${isDirectRow ? "border-amber-300 bg-amber-50 dark:border-amber-900/40 dark:bg-amber-900/20" : "border-gray-200 dark:border-graphite-border"}`}
                >
                  <div className="flex items-center justify-between gap-4">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <h4 className="font-medium text-gray-900 dark:text-graphite-text truncate text-sm">{product.productName}</h4>
                        {isDirectRow && (
                          <span className="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-amber-200 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300 flex-shrink-0">
                            Přímý výstup
                          </span>
                        )}
                      </div>
                      <div className="flex items-center gap-3 mt-1">
                        <p className="text-xs text-gray-600 dark:text-graphite-muted">{product.productCode}</p>
                        <p className="text-xs text-gray-500 dark:text-graphite-muted">
                          Plánované: <span className="font-medium">{product.plannedQuantity}{isDirectRow ? "g" : ""}</span>
                        </p>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 flex-shrink-0">
                      <label
                        htmlFor={`quantity-${product.id}`}
                        className="text-sm font-medium text-gray-700 dark:text-graphite-muted whitespace-nowrap"
                      >
                        Skutečné <span className="text-red-500 dark:text-red-400">*</span>
                      </label>
                      <div className="flex items-center gap-1">
                        <input
                          type="number"
                          id={`quantity-${product.id}`}
                          value={actualQuantities[product.id] || ''}
                          onChange={(e) => handleQuantityChange(product.id, e.target.value)}
                          className="w-20 px-2 py-1.5 border border-gray-300 rounded focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 text-center font-semibold text-sm dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                          min="0"
                          step={isDirectRow ? "0.1" : "1"}
                          disabled={isLoading}
                          required
                        />
                        {isDirectRow && <span className="text-sm text-gray-600 dark:text-graphite-muted">g</span>}
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Error Message */}
          {error && (
            <div className="flex items-center gap-2 p-2 bg-red-50 border border-red-200 dark:bg-red-900/20 dark:border-red-900/40 rounded">
              <AlertCircle className="h-4 w-4 text-red-600 dark:text-red-400 flex-shrink-0" />
              <span className="text-xs text-red-600 dark:text-red-400">{error}</span>
            </div>
          )}

          {/* Buttons */}
          <div className="flex gap-3 pt-2">
            <button
              type="button"
              onClick={handleClose}
              className="flex-1 px-3 py-2 text-gray-700 bg-gray-100 rounded hover:bg-gray-200 dark:text-graphite-muted dark:bg-graphite-surface-2 dark:hover:bg-graphite-hover transition-colors text-sm"
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
