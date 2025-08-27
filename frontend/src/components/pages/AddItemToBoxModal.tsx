import React, { useState } from 'react';
import { X, Package, Loader, AlertCircle } from 'lucide-react';
import { getAuthenticatedApiClient } from '../../api/client';
import MaterialAutocomplete from '../common/MaterialAutocomplete';
import { MaterialForPurchaseDto } from '../../api/hooks/useMaterials';
import { AddItemToBoxRequest } from '../../api/generated/api-client';

interface AddItemToBoxModalProps {
  isOpen: boolean;
  onClose: () => void;
  boxId: number | null;
  onSuccess: () => void;
}

const AddItemToBoxModal: React.FC<AddItemToBoxModalProps> = ({
  isOpen,
  onClose,
  boxId,
  onSuccess
}) => {
  const [selectedProduct, setSelectedProduct] = useState<MaterialForPurchaseDto | null>(null);
  const [amount, setAmount] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Helper function to provide user-friendly error messages
  const getUserFriendlyErrorMessage = (serverError: string): string => {
    const errorLower = serverError.toLowerCase();
    
    if (errorLower.includes('validation')) {
      return 'Neplatné údaje. Zkontrolujte zadané hodnoty.';
    }
    if (errorLower.includes('not found')) {
      return 'Box nebyl nalezen. Obnovte stránku a zkuste znovu.';
    }
    if (errorLower.includes('state')) {
      return 'Box není ve správném stavu pro přidání položky.';
    }
    if (errorLower.includes('network') || errorLower.includes('connection')) {
      return 'Chyba připojení. Zkontrolujte internetové připojení.';
    }
    if (errorLower.includes('timeout')) {
      return 'Operace trvá příliš dlouho. Zkuste to později.';
    }
    
    // Return original message for unrecognized errors
    return serverError;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxId) return;

    if (!selectedProduct || !selectedProduct.productCode || !selectedProduct.productName) {
      setError('Produkt je povinný');
      return;
    }

    const numAmount = parseFloat(amount);
    if (isNaN(numAmount) || numAmount <= 0) {
      setError('Množství musí být kladné číslo');
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const request = new AddItemToBoxRequest({
        productCode: selectedProduct.productCode,
        productName: selectedProduct.productName,
        amount: numAmount
      });
      const response = await apiClient.transportBox_AddItemToBox(boxId, request);

      if (response.success) {
        onSuccess();
        // Reset form
        setSelectedProduct(null);
        setAmount('');
        onClose();
      } else {
        const errorMessage = response.errorMessage || 'Chyba při přidávání položky';
        setError(getUserFriendlyErrorMessage(errorMessage));
      }
    } catch (err) {
      console.error('Error adding item to box:', err);
      const errorMessage = err instanceof Error ? err.message : 'Neočekávaná chyba';
      setError(getUserFriendlyErrorMessage(errorMessage));
    } finally {
      setIsLoading(false);
    }
  };

  const handleClose = () => {
    if (!isLoading) {
      setSelectedProduct(null);
      setAmount('');
      setError(null);
      onClose();
    }
  };

  const handleProductSelect = (product: MaterialForPurchaseDto | null) => {
    setSelectedProduct(product);
    setError(null);
  };

  if (!isOpen || !boxId) return null;

  return (
    <div className="fixed inset-0 bg-gray-500 bg-opacity-75 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
        <div className="flex items-center justify-between p-4 border-b border-gray-200">
          <div className="flex items-center">
            <Package className="h-5 w-5 text-indigo-600 mr-2" />
            <h2 className="text-lg font-medium text-gray-900">
              Přidání položky do boxu
            </h2>
          </div>
          <button
            onClick={handleClose}
            disabled={isLoading}
            aria-label="close"
            className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-4">
          {error && (
            <div className="mb-4 p-3 bg-red-50 border border-red-200 rounded-md flex items-center">
              <AlertCircle className="h-4 w-4 text-red-600 mr-2 flex-shrink-0" />
              <span className="text-sm text-red-800">{error}</span>
            </div>
          )}

          <div className="mb-4">
            <label htmlFor="product" className="block text-sm font-medium text-gray-700 mb-2">
              Produkt/Materiál
            </label>
            <MaterialAutocomplete
              value={selectedProduct}
              onSelect={handleProductSelect}
              disabled={isLoading}
              placeholder="Zadejte název nebo kód produktu..."
            />
          </div>

          <div className="mb-4">
            <label htmlFor="amount" className="block text-sm font-medium text-gray-700 mb-2">
              Množství
            </label>
            <input
              type="number"
              id="amount"
              value={amount}
              onChange={(e) => {
                setAmount(e.target.value);
                setError(null);
              }}
              disabled={isLoading}
              placeholder="0"
              min="0.01"
              step="0.01"
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent disabled:opacity-50 disabled:cursor-not-allowed"
            />
          </div>

          <div className="flex justify-end space-x-3">
            <button
              type="button"
              onClick={handleClose}
              disabled={isLoading}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={isLoading}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
            >
              {isLoading && <Loader className="h-4 w-4 mr-2 animate-spin" />}
              Přidat položku
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default AddItemToBoxModal;