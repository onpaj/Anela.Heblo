import React, { useState, useRef, useCallback } from 'react';
import { Package, Scan, Check, X, Loader2 } from 'lucide-react';
import { useToast } from '../../contexts/ToastContext';
import { useTransportBoxReceive } from '../../api/hooks/useTransportBoxReceive';
import { TransportBoxDto } from '../../api/generated/api-client';

const TransportBoxReceive: React.FC = () => {
  const [boxCode, setBoxCode] = useState('');
  const [boxDetails, setBoxDetails] = useState<TransportBoxDto | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isReceiving, setIsReceiving] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  
  const { showSuccess, showError } = useToast();
  const { getByCode, receive } = useTransportBoxReceive();

  // Auto-focus input field on component mount and after operations
  React.useEffect(() => {
    inputRef.current?.focus();
  }, []);

  // Clear form and refocus after successful operations
  const resetForm = useCallback(() => {
    setBoxCode('');
    setBoxDetails(null);
    setTimeout(() => inputRef.current?.focus(), 100);
  }, []);

  const handleScan = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!boxCode.trim()) {
      showError('Chyba', 'Zadejte kód boxu');
      return;
    }

    setIsLoading(true);
    
    try {
      const response = await getByCode(boxCode.trim());

      if (response.success && response.transportBox) {
        setBoxDetails(response.transportBox);
      } else {
        const errorMsg = response.errorCode ? `Chyba: ${response.errorCode}` : 'Chyba při načítání boxu';
        showError('Chyba', errorMsg);
        setBoxDetails(null);
      }
    } catch (error) {
      console.error('Error loading box:', error);
      showError('Chyba', 'Chyba při načítání boxu');
      setBoxDetails(null);
    } finally {
      setIsLoading(false);
    }
  };

  const handleReceive = async () => {
    if (!boxDetails || !boxDetails.id) return;

    setIsReceiving(true);

    try {
      const response = await receive(boxDetails.id, 'Warehouse User'); // TODO: Get actual user
      
      if (response.success) {
        showSuccess('Úspěch', `Box ${boxDetails.code || ''} úspěšně přijat`);
        resetForm();
      } else {
        const errorMsg = response.errorCode ? `Chyba: ${response.errorCode}` : 'Chyba při příjmu boxu';
        showError('Chyba', errorMsg);
      }
    } catch (error) {
      console.error('Error receiving box:', error);
      showError('Chyba', 'Chyba při příjmu boxu');
    } finally {
      setIsReceiving(false);
    }
  };

  const handleCancel = () => {
    resetForm();
  };

  const getStateBadgeColor = (state: string) => {
    switch (state) {
      case 'InTransit':
        return 'bg-blue-100 text-blue-800';
      case 'Reserve':
        return 'bg-yellow-100 text-yellow-800';
      case 'Received':
        return 'bg-green-100 text-green-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const getStateLabel = (state: string) => {
    switch (state) {
      case 'InTransit':
        return 'V přepravě';
      case 'Reserve':
        return 'V rezervě';
      case 'Received':
        return 'Přijatý';
      default:
        return state;
    }
  };

  return (
    <div className="space-y-6 pb-24">
      {/* Page Header with Barcode Scanner */}
      <div className="bg-white shadow rounded-lg p-6">
        <div className="flex items-start justify-between">
          <div className="flex items-center space-x-3">
            <Package className="h-8 w-8 text-indigo-600" />
            <div>
              <h1 className="text-2xl font-bold text-gray-900">
                Příjem transportních boxů
              </h1>
              <p className="mt-1 text-gray-600">
                Naskenujte kód boxu pro příjem zásilky do skladu
              </p>
            </div>
          </div>

          {/* Compact Barcode Scanner */}
          <form onSubmit={handleScan} className="flex items-center space-x-2 min-w-[400px]">
            <div className="relative flex-1">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Scan className="h-4 w-4 text-gray-400" />
              </div>
              <input
                ref={inputRef}
                type="text"
                id="boxCode"
                value={boxCode}
                onChange={(e) => setBoxCode(e.target.value.toUpperCase())}
                placeholder="Naskenujte kód boxu (např. B001)"
                className="block w-full pl-9 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-sm"
                disabled={isLoading}
                autoComplete="off"
              />
            </div>

            <button
              type="submit"
              disabled={isLoading || !boxCode.trim()}
              className="inline-flex items-center px-3 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap"
            >
              {isLoading ? (
                <>
                  <Loader2 className="animate-spin h-4 w-4 mr-1" />
                  Načítání
                </>
              ) : (
                'Načíst'
              )}
            </button>
          </form>
        </div>
      </div>

      {/* Box Details Card */}
      {boxDetails && (
        <div className="bg-white shadow rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center space-x-2">
              <h2 className="text-lg font-semibold text-gray-900">
                Detail boxu {boxDetails.code || ''}
              </h2>
              {boxDetails.lastStateChanged && (
                <span className="text-xs text-gray-500">
                  ({new Date(boxDetails.lastStateChanged).toLocaleString('cs-CZ')})
                </span>
              )}
            </div>
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateBadgeColor(boxDetails.state || '')}`}>
              {getStateLabel(boxDetails.state || '')}
            </span>
          </div>

          {boxDetails.location && (
            <div className="mb-4">
              <label className="block text-sm font-medium text-gray-500">Umístění</label>
              <p className="mt-1 text-sm text-gray-900">{boxDetails.location}</p>
            </div>
          )}

          {boxDetails.description && (
            <div className="mb-6 p-4 bg-yellow-50 border border-yellow-200 rounded-lg">
              <h3 className="text-sm font-medium text-yellow-800 mb-1">Poznámka</h3>
              <p className="text-sm text-yellow-700">{boxDetails.description}</p>
            </div>
          )}

          {/* Contents Card */}
          <div className="border border-gray-200 rounded-lg p-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">
              Obsah boxu ({boxDetails.items?.length || 0} {(boxDetails.items?.length || 0) === 1 ? 'položka' : (boxDetails.items?.length || 0) < 5 ? 'položky' : 'položek'})
            </h3>

            {!boxDetails.items || boxDetails.items.length === 0 ? (
              <p className="text-gray-500 text-sm">Box neobsahuje žádné položky</p>
            ) : (
              <div className="space-y-3">
                {boxDetails.items.map((item) => (
                  <div key={item.id} className="flex items-center space-x-4 p-3 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors">
                    {/* Product Image */}
                    <div className="flex-shrink-0 w-16 h-16 bg-white border border-gray-200 rounded-md overflow-hidden">
                      {item.imageUrl ? (
                        <img
                          src={item.imageUrl}
                          alt={item.productName}
                          className="w-full h-full object-cover"
                          onError={(e) => {
                            // Fallback to placeholder if image fails to load
                            (e.target as HTMLImageElement).src = 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"%3E%3Crect x="3" y="3" width="18" height="18" rx="2" ry="2"%3E%3C/rect%3E%3Ccircle cx="8.5" cy="8.5" r="1.5"%3E%3C/circle%3E%3Cpolyline points="21 15 16 10 5 21"%3E%3C/polyline%3E%3C/svg%3E';
                          }}
                        />
                      ) : (
                        <div className="w-full h-full flex items-center justify-center text-gray-400">
                          <Package className="h-8 w-8" />
                        </div>
                      )}
                    </div>

                    {/* Product Code - Small */}
                    <div className="flex-shrink-0 w-20">
                      <p className="text-xs text-gray-500 font-mono">
                        {item.productCode || ''}
                      </p>
                    </div>

                    {/* Product Name - Flexible */}
                    <div className="flex-1 min-w-0">
                      <p className="text-base font-medium text-gray-900 truncate">
                        {item.productName || ''}
                      </p>
                      <p className="text-xs text-gray-500 mt-0.5">
                        Skladem: {item.onStock || 0}
                      </p>
                    </div>

                    {/* Amount - Large and Prominent */}
                    <div className="flex-shrink-0 text-right">
                      <p className="text-2xl font-bold text-gray-900">
                        {item.amount || 0}
                      </p>
                      <p className="text-xs text-gray-500">ks</p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Action Buttons - Fixed to bottom with safe area for status bar */}
          <div className="fixed bottom-0 left-0 right-0 bg-white border-t border-gray-200 shadow-lg z-10 pb-8">
            <div className="max-w-7xl mx-auto flex justify-between space-x-4 p-4 pt-4 md:pl-64">
              <button
                type="button"
                onClick={handleCancel}
                className="flex-1 flex justify-center py-4 px-6 border border-gray-300 rounded-lg shadow-sm text-base font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 active:bg-gray-100 min-h-[56px]"
              >
                <X className="-ml-1 mr-2 h-5 w-5" />
                Storno
              </button>

              <button
                type="button"
                onClick={handleReceive}
                disabled={isReceiving}
                className="flex-1 flex justify-center py-4 px-6 border border-transparent rounded-lg shadow-sm text-base font-medium text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 disabled:opacity-50 disabled:cursor-not-allowed active:bg-green-800 min-h-[56px]"
              >
                {isReceiving ? (
                  <>
                    <Loader2 className="animate-spin -ml-1 mr-2 h-5 w-5" />
                    Přijímání...
                  </>
                ) : (
                  <>
                    <Check className="-ml-1 mr-2 h-5 w-5" />
                    Potvrdit příjem
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default TransportBoxReceive;