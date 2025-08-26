import React, { useState } from 'react';
import { X, Hash, Loader, AlertCircle } from 'lucide-react';
import { getAuthenticatedApiClient } from '../../api/client';
import { AssignBoxNumberRequest } from '../../api/generated/api-client';

interface AssignBoxNumberModalProps {
  isOpen: boolean;
  onClose: () => void;
  boxId: number | null;
  onSuccess: () => void;
}

const AssignBoxNumberModal: React.FC<AssignBoxNumberModalProps> = ({
  isOpen,
  onClose,
  boxId,
  onSuccess
}) => {
  const [boxNumber, setBoxNumber] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxId) return;

    const trimmedNumber = boxNumber.trim().toUpperCase();
    
    // Validate format: B + 3 digits
    if (!/^B\d{3}$/.test(trimmedNumber)) {
      setError('Číslo boxu musí být ve formátu B + 3 číslice (např. B001, B123)');
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const request = new AssignBoxNumberRequest({
        boxNumber: trimmedNumber
      });
      const response = await apiClient.transportBox_AssignBoxNumber(boxId, request);

      if (response.success) {
        onSuccess();
        setBoxNumber('');
        onClose();
      } else {
        setError(response.errorMessage || 'Chyba při přiřazování čísla boxu');
      }
    } catch (err) {
      console.error('Error assigning box number:', err);
      setError(err instanceof Error ? err.message : 'Neočekávaná chyba');
    } finally {
      setIsLoading(false);
    }
  };

  const handleClose = () => {
    if (!isLoading) {
      setBoxNumber('');
      setError(null);
      onClose();
    }
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const value = e.target.value.toUpperCase();
    setBoxNumber(value);
    setError(null);
  };

  if (!isOpen || !boxId) return null;

  return (
    <div className="fixed inset-0 bg-gray-500 bg-opacity-75 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
        <div className="flex items-center justify-between p-4 border-b border-gray-200">
          <div className="flex items-center">
            <Hash className="h-5 w-5 text-indigo-600 mr-2" />
            <h2 className="text-lg font-medium text-gray-900">
              Přiřazení čísla boxu
            </h2>
          </div>
          <button
            onClick={handleClose}
            disabled={isLoading}
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
            <label htmlFor="boxNumber" className="block text-sm font-medium text-gray-700 mb-2">
              Číslo boxu
            </label>
            <input
              type="text"
              id="boxNumber"
              value={boxNumber}
              onChange={handleInputChange}
              disabled={isLoading}
              placeholder="B001"
              maxLength={4}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent disabled:opacity-50 disabled:cursor-not-allowed font-mono"
            />
            <p className="mt-1 text-xs text-gray-500">
              Formát: B + 3 číslice (např. B001, B123). Box přejde do stavu "Otevřený".
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
              disabled={isLoading || !boxNumber.trim()}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
            >
              {isLoading && <Loader className="h-4 w-4 mr-2 animate-spin" />}
              Přiřadit číslo
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default AssignBoxNumberModal;