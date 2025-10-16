import React, { useState, useRef, useCallback } from 'react';
import { Package, Scan, Check, X, Loader2 } from 'lucide-react';
import { useToast } from '../../contexts/ToastContext';
import { useTransportBoxReceive } from '../../api/hooks/useTransportBoxReceive';

interface BoxDetails {
  id: number;
  code: string;
  state: string;
  location?: string;
  description?: string;
  lastStateChanged?: string;
  items: Array<{
    id: number;
    productCode: string;
    productName: string;
    amount: number;
  }>;
}

const TransportBoxReceive: React.FC = () => {
  const [boxCode, setBoxCode] = useState('');
  const [boxDetails, setBoxDetails] = useState<BoxDetails | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isReceiving, setIsReceiving] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  
  const { showSuccess, showError, showInfo } = useToast();
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
        setBoxDetails({
          id: response.transportBox.id,
          code: response.transportBox.code,
          state: response.transportBox.state,
          location: response.transportBox.location,
          description: response.transportBox.description,
          lastStateChanged: response.transportBox.lastStateChanged,
          items: response.transportBox.items
        });
        showSuccess('Úspěch', 'Box nalezen a připraven k příjmu');
      } else {
        showError('Chyba', response.errorMessage || 'Chyba při načítání boxu');
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
    if (!boxDetails) return;

    setIsReceiving(true);
    
    try {
      const response = await receive(boxDetails.id, 'Warehouse User'); // TODO: Get actual user
      
      if (response.success) {
        showSuccess('Úspěch', `Box ${boxDetails.code} úspěšně přijat`);
        resetForm();
      } else {
        showError('Chyba', response.errorMessage || 'Chyba při příjmu boxu');
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
    showInfo('Info', 'Operace zrušena');
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
    <div className="space-y-6">
      {/* Page Header */}
      <div className="bg-white shadow rounded-lg p-6">
        <div className="flex items-center space-x-3">
          <Package className="h-8 w-8 text-indigo-600" />
          <h1 className="text-2xl font-bold text-gray-900">
            Příjem transportních boxů
          </h1>
        </div>
        <p className="mt-2 text-gray-600">
          Naskenujte kód boxu pro příjem zásilky do skladu
        </p>
      </div>

      {/* Barcode Input Card */}
      <div className="bg-white shadow rounded-lg p-6">
        <form onSubmit={handleScan} className="space-y-4">
          <div>
            <label htmlFor="boxCode" className="block text-sm font-medium text-gray-700 mb-2">
              Kód boxu
            </label>
            <div className="relative">
              <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                <Scan className="h-5 w-5 text-gray-400" />
              </div>
              <input
                ref={inputRef}
                type="text"
                id="boxCode"
                value={boxCode}
                onChange={(e) => setBoxCode(e.target.value.toUpperCase())}
                placeholder="Naskenujte nebo zadejte kód boxu (např. B001)"
                className="block w-full pl-10 pr-3 py-3 border border-gray-300 rounded-md leading-5 bg-white placeholder-gray-500 focus:outline-none focus:placeholder-gray-400 focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 text-lg"
                disabled={isLoading}
                autoComplete="off"
              />
            </div>
          </div>
          
          <button
            type="submit"
            disabled={isLoading || !boxCode.trim()}
            className="w-full flex justify-center py-3 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isLoading ? (
              <>
                <Loader2 className="animate-spin -ml-1 mr-2 h-4 w-4" />
                Načítání...
              </>
            ) : (
              <>
                <Scan className="-ml-1 mr-2 h-4 w-4" />
                Načíst box
              </>
            )}
          </button>
        </form>
      </div>

      {/* Box Details Card */}
      {boxDetails && (
        <div className="bg-white shadow rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <h2 className="text-lg font-semibold text-gray-900">
              Detail boxu {boxDetails.code}
            </h2>
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateBadgeColor(boxDetails.state)}`}>
              {getStateLabel(boxDetails.state)}
            </span>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
            <div>
              <label className="block text-sm font-medium text-gray-500">Stav</label>
              <p className="mt-1 text-sm text-gray-900">{getStateLabel(boxDetails.state)}</p>
            </div>
            {boxDetails.location && (
              <div>
                <label className="block text-sm font-medium text-gray-500">Umístění</label>
                <p className="mt-1 text-sm text-gray-900">{boxDetails.location}</p>
              </div>
            )}
            {boxDetails.lastStateChanged && (
              <div>
                <label className="block text-sm font-medium text-gray-500">Poslední změna</label>
                <p className="mt-1 text-sm text-gray-900">
                  {new Date(boxDetails.lastStateChanged).toLocaleString('cs-CZ')}
                </p>
              </div>
            )}
          </div>

          {boxDetails.description && (
            <div className="mb-6 p-4 bg-yellow-50 border border-yellow-200 rounded-lg">
              <h3 className="text-sm font-medium text-yellow-800 mb-1">Poznámka</h3>
              <p className="text-sm text-yellow-700">{boxDetails.description}</p>
            </div>
          )}

          {/* Contents Card */}
          <div className="border border-gray-200 rounded-lg p-4">
            <h3 className="text-md font-medium text-gray-900 mb-3">
              Obsah boxu ({boxDetails.items.length} položek)
            </h3>
            
            {boxDetails.items.length === 0 ? (
              <p className="text-gray-500 text-sm">Box neobsahuje žádné položky</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-200">
                  <thead className="bg-gray-50">
                    <tr>
                      <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Kód produktu
                      </th>
                      <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Název
                      </th>
                      <th className="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                        Množství
                      </th>
                    </tr>
                  </thead>
                  <tbody className="bg-white divide-y divide-gray-200">
                    {boxDetails.items.map((item) => (
                      <tr key={item.id}>
                        <td className="px-3 py-2 whitespace-nowrap text-sm font-medium text-gray-900">
                          {item.productCode}
                        </td>
                        <td className="px-3 py-2 whitespace-nowrap text-sm text-gray-900">
                          {item.productName}
                        </td>
                        <td className="px-3 py-2 whitespace-nowrap text-sm text-gray-900 text-right">
                          {item.amount}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Action Buttons */}
          <div className="flex justify-between mt-6 space-x-4">
            <button
              type="button"
              onClick={handleCancel}
              className="flex-1 flex justify-center py-3 px-4 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
            >
              <X className="-ml-1 mr-2 h-4 w-4" />
              Storno
            </button>
            
            <button
              type="button"
              onClick={handleReceive}
              disabled={isReceiving}
              className="flex-1 flex justify-center py-3 px-4 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isReceiving ? (
                <>
                  <Loader2 className="animate-spin -ml-1 mr-2 h-4 w-4" />
                  Přijímání...
                </>
              ) : (
                <>
                  <Check className="-ml-1 mr-2 h-4 w-4" />
                  Potvrdit příjem
                </>
              )}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default TransportBoxReceive;