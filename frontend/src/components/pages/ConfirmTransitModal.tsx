import React, { useState } from 'react';
import { X, Truck, Loader, AlertCircle, Hash } from 'lucide-react';
import { getAuthenticatedApiClient } from '../../api/client';
import { ConfirmTransitRequest } from '../../api/generated/api-client';

interface ConfirmTransitModalProps {
  isOpen: boolean;
  onClose: () => void;
  boxId: number | null;
  boxCode: string | null;
  onSuccess: () => void;
}

const ConfirmTransitModal: React.FC<ConfirmTransitModalProps> = ({
  isOpen,
  onClose,
  boxId,
  boxCode,
  onSuccess
}) => {
  const [confirmedBoxNumber, setConfirmedBoxNumber] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxId || !boxCode) return;

    const trimmedNumber = confirmedBoxNumber.trim().toUpperCase();

    if (!trimmedNumber) {
      setError('Potvrzení čísla boxu je povinné');
      return;
    }

    if (trimmedNumber !== boxCode) {
      setError(`Zadané číslo neodpovídá číslu boxu (${boxCode})`);
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const apiClient = await getAuthenticatedApiClient();
      const request = new ConfirmTransitRequest({
        confirmationBoxNumber: trimmedNumber
      });
      const response = await apiClient.transportBox_ConfirmTransit(boxId, request);

      if (response.success) {
        onSuccess();
        setConfirmedBoxNumber('');
        onClose();
      } else {
        setError(response.errorMessage || 'Chyba při potvrzování přepravy');
      }
    } catch (err) {
      console.error('Error confirming transit:', err);
      setError(err instanceof Error ? err.message : 'Neočekávaná chyba');
    } finally {
      setIsLoading(false);
    }
  };

  const handleClose = () => {
    if (!isLoading) {
      setConfirmedBoxNumber('');
      setError(null);
      onClose();
    }
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const value = e.target.value.toUpperCase();
    setConfirmedBoxNumber(value);
    setError(null);
  };

  if (!isOpen || !boxId || !boxCode) return null;

  return (
    <div className="fixed inset-0 bg-gray-500 bg-opacity-75 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
        <div className="flex items-center justify-between p-4 border-b border-gray-200">
          <div className="flex items-center">
            <Truck className="h-5 w-5 text-indigo-600 mr-2" />
            <h2 className="text-lg font-medium text-gray-900">
              Potvrzení přepravy
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

        <div className="p-4">
          <div className="mb-4 p-4 bg-yellow-50 border border-yellow-200 rounded-md">
            <div className="flex items-center">
              <AlertCircle className="h-5 w-5 text-yellow-600 mr-2 flex-shrink-0" />
              <div>
                <h3 className="text-sm font-medium text-yellow-800">
                  Pozor: Po potvrzení nelze upravit obsah boxu
                </h3>
                <p className="text-sm text-yellow-700 mt-1">
                  Box přejde do stavu "V přepravě" a obsahuje položky budou uzamčené.
                </p>
              </div>
            </div>
          </div>

          <form onSubmit={handleSubmit}>
            {error && (
              <div className="mb-4 p-3 bg-red-50 border border-red-200 rounded-md flex items-center">
                <AlertCircle className="h-4 w-4 text-red-600 mr-2 flex-shrink-0" />
                <span className="text-sm text-red-800">{error}</span>
              </div>
            )}

            <div className="mb-4">
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Číslo boxu pro potvrzení
              </label>
              <div className="mb-2 p-3 bg-gray-50 rounded-md">
                <div className="flex items-center">
                  <Hash className="h-4 w-4 text-gray-400 mr-2" />
                  <span className="text-sm text-gray-600">
                    Očekávané číslo boxu:
                  </span>
                  <span className="ml-2 font-mono font-medium text-gray-900">
                    {boxCode}
                  </span>
                </div>
              </div>
              <input
                type="text"
                value={confirmedBoxNumber}
                onChange={handleInputChange}
                disabled={isLoading}
                placeholder={`Zadejte ${boxCode} pro potvrzení`}
                maxLength={4}
                className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent disabled:opacity-50 disabled:cursor-not-allowed font-mono"
              />
              <p className="mt-1 text-xs text-gray-500">
                Zadejte číslo boxu znovu pro potvrzení přechodu do přepravy.
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
                disabled={isLoading || !confirmedBoxNumber.trim()}
                className="px-4 py-2 text-sm font-medium text-white bg-red-600 border border-transparent rounded-md hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
              >
                {isLoading && <Loader className="h-4 w-4 mr-2 animate-spin" />}
                Potvrdit přepravu
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  );
};

export default ConfirmTransitModal;