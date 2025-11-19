import React, { useState } from 'react';
import { Calendar, X, TrendingDown } from 'lucide-react';
import { LoadingIndicator } from '../../ui/LoadingIndicator';
import { useProcessDailyConsumption } from '../../../api/hooks/usePackingMaterials';
import { ProcessDailyConsumptionRequest } from '../../../api/generated/api-client';

interface ProcessDailyConsumptionModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: () => void;
}

const ProcessDailyConsumptionModal: React.FC<ProcessDailyConsumptionModalProps> = ({
  isOpen,
  onClose,
  onSuccess
}) => {
  const [selectedDate, setSelectedDate] = useState<string>(() => {
    // Default to yesterday
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    return yesterday.toISOString().split('T')[0];
  });
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const processDailyConsumptionMutation = useProcessDailyConsumption();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSuccessMessage(null);

    try {
      const request = new ProcessDailyConsumptionRequest({
        processingDate: new Date(selectedDate)
      });
      const result = await processDailyConsumptionMutation.mutateAsync(request);
      
      if (result.success) {
        setSuccessMessage(result.message || 'Spotřeba byla úspěšně odečtena');
        setTimeout(() => {
          onSuccess?.();
          onClose();
        }, 2000);
      }
    } catch (error) {
      console.error('Error processing daily consumption:', error);
      // Error is handled by the mutation and displayed below
    }
  };

  const handleClose = () => {
    if (!processDailyConsumptionMutation.isPending) {
      setSuccessMessage(null);
      onClose();
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <TrendingDown className="h-6 w-6 text-indigo-600" />
            <h3 className="text-lg font-medium text-gray-900">Odečíst spotřebu</h3>
          </div>
          <button
            onClick={handleClose}
            disabled={processDailyConsumptionMutation.isPending}
            className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <form onSubmit={handleSubmit} className="p-6 space-y-6">
          {/* Date Picker */}
          <div>
            <label htmlFor="date" className="block text-sm font-medium text-gray-700 mb-2">
              Vyberte datum pro odečtení spotřeby
            </label>
            <div className="relative">
              <Calendar className="absolute left-3 top-3 h-5 w-5 text-gray-400" />
              <input
                type="date"
                id="date"
                value={selectedDate}
                onChange={(e) => setSelectedDate(e.target.value)}
                className="block w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
                required
                disabled={processDailyConsumptionMutation.isPending}
              />
            </div>
            <p className="mt-2 text-sm text-gray-500">
              Systém načte faktury za vybraný den a odečte odpovídající spotřebu materiálů
            </p>
          </div>

          {/* Error Message */}
          {processDailyConsumptionMutation.error && (
            <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
              {processDailyConsumptionMutation.error.message || 'Nepodařilo se odečíst spotřebu. Zkuste to znovu.'}
            </div>
          )}

          {/* Success Message */}
          {successMessage && (
            <div className="bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded">
              {successMessage}
            </div>
          )}

          {/* Buttons */}
          <div className="flex space-x-3">
            <button
              type="button"
              onClick={handleClose}
              disabled={processDailyConsumptionMutation.isPending}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={processDailyConsumptionMutation.isPending}
              className="flex-1 inline-flex items-center justify-center px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 disabled:opacity-50"
            >
              {processDailyConsumptionMutation.isPending ? (
                <>
                  <LoadingIndicator isVisible={true} />
                  <span className="ml-2">Zpracovávám...</span>
                </>
              ) : (
                <>
                  <TrendingDown className="h-4 w-4 mr-2" />
                  Odečíst spotřebu
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default ProcessDailyConsumptionModal;