import React, { useState, useEffect } from 'react';
import { X, RotateCcw, Loader, AlertCircle } from 'lucide-react';
import { getAuthenticatedApiClient } from '../../api/client';
import { AddItemToBoxRequest } from '../../api/generated/api-client';
import { LastAddedItem } from '../../api/hooks/useLastAddedItem';

interface QuickAddLastItemModalProps {
  isOpen: boolean;
  onClose: () => void;
  boxId: number | null;
  lastAddedItem: LastAddedItem | null;
  onSuccess: () => void;
  onItemAdded?: (item: Omit<LastAddedItem, 'timestamp'>) => void;
}

const QuickAddLastItemModal: React.FC<QuickAddLastItemModalProps> = ({
  isOpen,
  onClose,
  boxId,
  lastAddedItem,
  onSuccess,
  onItemAdded
}) => {
  const [amount, setAmount] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Initialize amount with last added item amount when modal opens
  useEffect(() => {
    if (isOpen && lastAddedItem) {
      setAmount(lastAddedItem.amount.toString());
    }
  }, [isOpen, lastAddedItem]);

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
    if (!boxId || !lastAddedItem) return;

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
        productCode: lastAddedItem.productCode,
        productName: lastAddedItem.productName,
        amount: numAmount
      });
      const response = await apiClient.transportBox_AddItemToBox(boxId, request);

      if (response.success) {
        // Call onItemAdded to update last added item if provided
        if (onItemAdded) {
          onItemAdded({
            productCode: lastAddedItem.productCode,
            productName: lastAddedItem.productName,
            amount: numAmount
          });
        }
        
        onSuccess();
        onClose();
      }
      // If response.success is false, the global error handler will show a toast
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
      setAmount('');
      setError(null);
      onClose();
    }
  };

  if (!isOpen || !boxId || !lastAddedItem) return null;

  return (
    <div className="fixed inset-0 bg-gray-500 bg-opacity-75 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
        <div className="flex items-center justify-between p-4 border-b border-gray-200">
          <div className="flex items-center">
            <RotateCcw className="h-5 w-5 text-emerald-600 mr-2" />
            <h2 className="text-lg font-medium text-gray-900">
              Opakovat poslední položku
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
          {error && (
            <div className="mb-4 p-3 bg-red-50 border border-red-200 rounded-md flex items-center">
              <AlertCircle className="h-4 w-4 text-red-600 mr-2 flex-shrink-0" />
              <span className="text-sm text-red-800">{error}</span>
            </div>
          )}

          {/* Show the product info */}
          <div className="mb-4 p-3 bg-emerald-50 border border-emerald-200 rounded-md">
            <div className="flex items-center">
              <RotateCcw className="h-4 w-4 text-emerald-600 mr-2 flex-shrink-0" />
              <div className="flex-1">
                <p className="text-sm font-medium text-emerald-900">
                  {lastAddedItem.productName}
                </p>
                <p className="text-xs text-emerald-700 font-mono">
                  {lastAddedItem.productCode}
                </p>
              </div>
            </div>
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
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:border-transparent disabled:opacity-50 disabled:cursor-not-allowed"
              autoFocus
            />
            <p className="mt-1 text-xs text-gray-500">
              Původní množství: {lastAddedItem.amount}
            </p>
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
              className="px-4 py-2 text-sm font-medium text-white bg-emerald-600 border border-transparent rounded-md hover:bg-emerald-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
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

export default QuickAddLastItemModal;