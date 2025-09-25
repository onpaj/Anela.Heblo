import React, { useState, useEffect } from "react";
import { X, AlertCircle, Factory, Loader2 } from "lucide-react";
import { ConfirmSemiProductManufactureRequest } from "../../api/generated/api-client";

interface ConfirmSemiProductQuantityModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (request: ConfirmSemiProductManufactureRequest) => Promise<void>;
  orderId: number;
  plannedQuantity: number;
  productName: string;
  isLoading?: boolean;
}

const ConfirmSemiProductQuantityModal: React.FC<ConfirmSemiProductQuantityModalProps> = ({
  isOpen,
  onClose,
  onSubmit,
  orderId,
  plannedQuantity,
  productName,
  isLoading = false,
}) => {
  const [actualQuantity, setActualQuantity] = useState<string>("");
  const [error, setError] = useState<string>("");

  // Initialize with planned quantity when modal opens
  useEffect(() => {
    if (isOpen) {
      setActualQuantity(plannedQuantity.toString());
      setError("");
    }
  }, [isOpen, plannedQuantity]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

    const quantity = parseFloat(actualQuantity);
    if (!actualQuantity || isNaN(quantity) || quantity <= 0) {
      setError("Zadejte platné množství větší než 0");
      return;
    }

    try {
      const request = new ConfirmSemiProductManufactureRequest({
        id: orderId,
        actualQuantity: quantity,
        changeReason: `Potvrzeno skutečné množství polotovaru: ${quantity}`
      });

      await onSubmit(request);
      
      // Reset form
      setActualQuantity("");
      setError("");
    } catch (err) {
      setError("Chyba při potvrzení množství. Zkuste to prosím znovu.");
      console.error("Error confirming semi-product quantity:", err);
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

  if (!isOpen) return null;

  return (
    <div 
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-[60]"
      onClick={(e) => e.target === e.currentTarget && handleClose()}
      onKeyDown={handleKeyDown}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <h2 className="text-xl font-semibold text-gray-900 flex items-center gap-2">
            <Factory className="h-5 w-5 text-indigo-600" />
            Potvrdit skutečné množství
          </h2>
          <button
            onClick={handleClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
            disabled={isLoading}
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Content */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {/* Product info */}
          <div className="bg-blue-50 rounded-lg p-4">
            <h3 className="font-medium text-gray-900 mb-1">Polotovar</h3>
            <p className="text-sm text-gray-600">{productName}</p>
            <p className="text-xs text-gray-500 mt-1">
              Plánované množství: <span className="font-medium">{plannedQuantity}</span>
            </p>
          </div>

          {/* Actual Quantity Input */}
          <div>
            <label htmlFor="actualQuantity" className="block text-sm font-medium text-gray-700 mb-2">
              Skutečné množství <span className="text-red-500">*</span>
            </label>
            <input
              type="number"
              id="actualQuantity"
              value={actualQuantity}
              onChange={(e) => setActualQuantity(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 text-center text-lg font-semibold"
              min="1"
              step="1"
              disabled={isLoading}
              autoFocus
              required
            />
          </div>

          {/* Error Message */}
          {error && (
            <div className="flex items-center gap-2 p-3 bg-red-50 border border-red-200 rounded-lg">
              <AlertCircle className="h-4 w-4 text-red-600 flex-shrink-0" />
              <span className="text-sm text-red-600">{error}</span>
            </div>
          )}

          {/* Buttons */}
          <div className="flex gap-3 pt-4">
            <button
              type="button"
              onClick={handleClose}
              className="flex-1 px-4 py-2 text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
              disabled={isLoading}
            >
              Zrušit
            </button>
            <button
              type="submit"
              className="flex-1 flex items-center justify-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
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

export default ConfirmSemiProductQuantityModal;