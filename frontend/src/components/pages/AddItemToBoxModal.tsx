import React, { useState } from 'react';
import { X, Package, Loader } from 'lucide-react';
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxId) return;

    if (!selectedProduct || !selectedProduct.productCode || !selectedProduct.productName) {
      alert('Produkt je povinný');
      return;
    }

    const numAmount = parseFloat(amount);
    if (isNaN(numAmount) || numAmount <= 0) {
      alert('Množství musí být kladné číslo');
      return;
    }

    setIsLoading(true);

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
      }
      // If response.success is false, the global error handler will show a toast
    } catch (err) {
      // Network errors or other exceptions - global handler will show toast
      console.error('Error adding item to box:', err);
    } finally {
      setIsLoading(false);
    }
  };

  const handleClose = () => {
    if (!isLoading) {
      setSelectedProduct(null);
      setAmount('');
      onClose();
    }
  };

  const handleProductSelect = (product: MaterialForPurchaseDto | null) => {
    setSelectedProduct(product);
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

        <form onSubmit={handleSubmit} className="p-4" onKeyDown={(e) => {
          if (e.key === 'Enter' && e.target instanceof HTMLInputElement && e.target.type === 'number') {
            e.preventDefault();
            handleSubmit(e as any);
          }
        }}>
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
              onChange={(e) => setAmount(e.target.value)}
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