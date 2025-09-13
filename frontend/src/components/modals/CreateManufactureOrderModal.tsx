import React, { useState } from "react";
import { X, Calendar, User, Package, AlertCircle } from "lucide-react";
import { CalculatedBatchSizeResponse, CreateManufactureOrderRequest, CreateManufactureOrderIngredientRequest } from "../../api/generated/api-client";

interface CreateManufactureOrderModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (request: CreateManufactureOrderRequest) => Promise<void>;
  batchResult: CalculatedBatchSizeResponse;
  isLoading?: boolean;
}

const CreateManufactureOrderModal: React.FC<CreateManufactureOrderModalProps> = ({
  isOpen,
  onClose,
  onSubmit,
  batchResult,
  isLoading = false,
}) => {
  const [semiProductDate, setSemiProductDate] = useState<string>("");
  const [productDate, setProductDate] = useState<string>("");
  const [responsiblePerson, setResponsiblePerson] = useState<string>("");
  const [error, setError] = useState<string>("");

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");

    if (!semiProductDate) {
      setError("Datum výroby polotovaru je povinný");
      return;
    }

    if (!productDate) {
      setError("Datum výroby produktu je povinný");
      return;
    }

    const semiProductDateObj = new Date(semiProductDate);
    const productDateObj = new Date(productDate);

    if (productDateObj <= semiProductDateObj) {
      setError("Datum výroby produktu musí být později než datum výroby polotovaru");
      return;
    }

    try {
      const ingredients: CreateManufactureOrderIngredientRequest[] = 
        batchResult.ingredients?.map(ingredient => new CreateManufactureOrderIngredientRequest({
          productCode: ingredient.productCode!,
          productName: ingredient.productName!,
          originalAmount: ingredient.originalAmount!,
          calculatedAmount: ingredient.calculatedAmount!
        })) || [];

      const request = new CreateManufactureOrderRequest({
        productCode: batchResult.productCode!,
        productName: batchResult.productName!,
        originalBatchSize: batchResult.originalBatchSize!,
        newBatchSize: batchResult.newBatchSize!,
        scaleFactor: batchResult.scaleFactor!,
        ingredients: ingredients,
        semiProductPlannedDate: semiProductDate as any, // Will be converted to DateOnly on backend
        productPlannedDate: productDate as any, // Will be converted to DateOnly on backend
        responsiblePerson: responsiblePerson || undefined
      });

      await onSubmit(request);
      
      // Reset form
      setSemiProductDate("");
      setProductDate("");
      setResponsiblePerson("");
      setError("");
    } catch (err) {
      setError("Chyba při vytváření zakázky. Zkuste to prosím znovu.");
      console.error("Error creating manufacture order:", err);
    }
  };

  const handleClose = () => {
    setError("");
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-2xl w-full mx-4 max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <h2 className="text-xl font-semibold text-gray-900 flex items-center gap-2">
            <Package className="h-5 w-5 text-indigo-600" />
            Vytvořit výrobní zakázku
          </h2>
          <button
            onClick={handleClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
            disabled={isLoading}
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6">
          {/* Batch Summary */}
          <div className="bg-gray-50 rounded-lg p-4 mb-6">
            <h3 className="text-sm font-medium text-gray-900 mb-3">Shrnutí dávky</h3>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-sm">
              <div>
                <span className="text-gray-500">Produkt:</span>
                <div className="font-medium">{batchResult.productName}</div>
                <div className="text-gray-500 font-mono text-xs">{batchResult.productCode}</div>
              </div>
              <div>
                <span className="text-gray-500">Původní dávka:</span>
                <div className="font-medium">{batchResult.originalBatchSize}g</div>
              </div>
              <div>
                <span className="text-gray-500">Nová dávka:</span>
                <div className="font-medium text-indigo-600">{batchResult.newBatchSize}g</div>
              </div>
            </div>
          </div>

          {/* Form */}
          <form onSubmit={handleSubmit} className="space-y-6">
            {/* Error Message */}
            {error && (
              <div className="bg-red-50 border border-red-200 rounded-md p-4 flex items-start gap-3">
                <AlertCircle className="h-5 w-5 text-red-500 flex-shrink-0 mt-0.5" />
                <div className="text-sm text-red-700">{error}</div>
              </div>
            )}

            {/* Planning Dates */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">
                  <Calendar className="h-4 w-4 inline mr-1" />
                  Plánovaný datum výroby polotovaru *
                </label>
                <input
                  type="date"
                  value={semiProductDate}
                  onChange={(e) => setSemiProductDate(e.target.value)}
                  required
                  disabled={isLoading}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:opacity-50"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Kdy plánujete dokončit výrobu polotovaru
                </p>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">
                  <Calendar className="h-4 w-4 inline mr-1" />
                  Plánovaný datum výroby produktu *
                </label>
                <input
                  type="date"
                  value={productDate}
                  onChange={(e) => setProductDate(e.target.value)}
                  required
                  disabled={isLoading}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:opacity-50"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Kdy plánujete dokončit finální produkty
                </p>
              </div>
            </div>

            {/* Responsible Person */}
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                <User className="h-4 w-4 inline mr-1" />
                Odpovědná osoba
              </label>
              <input
                type="text"
                value={responsiblePerson}
                onChange={(e) => setResponsiblePerson(e.target.value)}
                placeholder="Jméno odpovědné osoby (volitelné)"
                disabled={isLoading}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:opacity-50"
              />
              <p className="text-xs text-gray-500 mt-1">
                Osoba zodpovědná za realizaci zakázky
              </p>
            </div>

            {/* Ingredients Summary */}
            <div>
              <h4 className="text-sm font-medium text-gray-900 mb-3">
                Ingredience ({batchResult.ingredients?.length || 0})
              </h4>
              <div className="bg-gray-50 rounded-md p-3 max-h-40 overflow-y-auto">
                <div className="space-y-2">
                  {batchResult.ingredients?.map((ingredient, index) => (
                    <div key={ingredient.productCode} className="flex justify-between text-sm">
                      <span className="text-gray-700">{ingredient.productName}</span>
                      <span className="font-medium">{ingredient.calculatedAmount?.toFixed(2)}g</span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </form>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 p-6 border-t border-gray-200 bg-gray-50">
          <button
            type="button"
            onClick={handleClose}
            disabled={isLoading}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:opacity-50"
          >
            Zrušit
          </button>
          <button
            type="submit"
            onClick={handleSubmit}
            disabled={isLoading || !semiProductDate || !productDate}
            className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 disabled:opacity-50 flex items-center gap-2"
          >
            {isLoading ? (
              <>
                <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                Vytváří se...
              </>
            ) : (
              <>
                <Package className="h-4 w-4" />
                Vytvořit zakázku
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};

export default CreateManufactureOrderModal;