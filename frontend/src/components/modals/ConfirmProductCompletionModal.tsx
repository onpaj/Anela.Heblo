import React, { useState, useEffect } from "react";
import { X, AlertCircle, Package, Loader2 } from "lucide-react";
import { ConfirmProductCompletionRequest, ProductActualQuantityRequest } from "../../api/generated/api-client";

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
}

const ConfirmProductCompletionModal: React.FC<ConfirmProductCompletionModalProps> = ({
  isOpen,
  onClose,
  onSubmit,
  orderId,
  products,
  isLoading = false,
}) => {
  const [actualQuantities, setActualQuantities] = useState<{ [key: number]: string }>({});
  const [error, setError] = useState<string>("");

  // Initialize with planned quantities when modal opens
  useEffect(() => {
    if (isOpen && products.length > 0) {
      const initialQuantities: { [key: number]: string } = {};
      products.forEach(product => {
        initialQuantities[product.id] = product.plannedQuantity.toString();
      });
      setActualQuantities(initialQuantities);
      setError("");
    }
  }, [isOpen, products]);

  const handleQuantityChange = (productId: number, value: string) => {
    setActualQuantities(prev => ({
      ...prev,
      [productId]: value
    }));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

    // Validate all quantities
    const productRequests: ProductActualQuantityRequest[] = [];
    for (const product of products) {
      const quantityStr = actualQuantities[product.id];
      const quantity = parseFloat(quantityStr);
      
      if (quantityStr === undefined || quantityStr === "" || isNaN(quantity) || quantity < 0) {
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
        changeReason: "Potvrzeno dokončení výroby produktů"
      });

      await onSubmit(request);
      
      // Reset form
      setActualQuantities({});
      setError("");
    } catch (err) {
      setError("Chyba při potvrzení množství produktů. Zkuste to prosím znovu.");
      console.error("Error confirming product completion:", err);
    }
  };

  const handleClose = () => {
    setError("");
    onClose();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      handleClose();
    }
  };

  if (!isOpen || products.length === 0) return null;

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
                      value={actualQuantities[product.id] || ""}
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
                "Potvrdit výrobu"
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default ConfirmProductCompletionModal;