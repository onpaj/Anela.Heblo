import React, { useEffect, useState } from 'react';
import { X, Package, Calendar, MapPin, User, Clock, Box, Tag, AlertCircle, ArrowLeft, ArrowRight, Loader2 } from 'lucide-react';
import { useTransportBoxByIdQuery, useChangeTransportBoxState } from '../../api/hooks/useTransportBoxes';

interface TransportBoxDetailProps {
  boxId: number | null;
  isOpen: boolean;
  onClose: () => void;
}

// State labels mapping
const stateLabels: Record<string, string> = {
  'New': 'Nový',
  'Opened': 'Otevřený',
  'InTransit': 'V přepravě',
  'Received': 'Přijatý',
  'InSwap': 'Swap',
  'Stocked': 'Naskladněný',
  'Reserve': 'V rezervě',
  'Closed': 'Uzavřený',
  'Error': 'Chyba',
};

// Define state transitions based on backend logic
const stateTransitions: Record<string, { next?: string; previous?: string }> = {
  'New': { next: 'Opened', previous: 'Closed' },
  'Opened': { next: 'InTransit', previous: 'New' },
  'InTransit': { next: 'Received', previous: 'Opened' },
  'Received': {}, // No automatic transitions
  'InSwap': { next: 'Stocked' },
  'Stocked': { next: 'Closed' },
  'Closed': {},
  'Reserve': { next: 'Received', previous: 'Opened' },
  'Error': {},
};

const stateColors: Record<string, string> = {
  'New': 'bg-gray-100 text-gray-800',
  'Opened': 'bg-blue-100 text-blue-800',
  'InTransit': 'bg-yellow-100 text-yellow-800',
  'Received': 'bg-purple-100 text-purple-800',
  'InSwap': 'bg-orange-100 text-orange-800',
  'Stocked': 'bg-green-100 text-green-800',
  'Reserve': 'bg-indigo-100 text-indigo-800',
  'Closed': 'bg-gray-100 text-gray-800',
  'Error': 'bg-red-100 text-red-800',
};

const TransportBoxDetail: React.FC<TransportBoxDetailProps> = ({ boxId, isOpen, onClose }) => {
  const { data: boxData, isLoading, error } = useTransportBoxByIdQuery(boxId || 0, boxId !== null);
  const [activeTab, setActiveTab] = useState<'items' | 'history'>('items');
  const changeStateMutation = useChangeTransportBoxState();

  // Handle Escape key to close modal
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleKeyDown);
      return () => {
        document.removeEventListener('keydown', handleKeyDown);
      };
    }
  }, [isOpen, onClose]);

  // Reset tab when modal opens
  useEffect(() => {
    if (isOpen) {
      setActiveTab('items');
    }
  }, [isOpen]);

  // Handle state change
  const handleStateChange = async (newState: string) => {
    if (!boxId) return;
    
    try {
      await changeStateMutation.mutateAsync({
        boxId,
        newState,
        description: `State changed to ${stateLabels[newState] || newState}`
      });
    } catch (error) {
      console.error('Failed to change state:', error);
      // TODO: Add toast notification for error
    }
  };

  // Get available state transitions
  const getAvailableTransitions = (currentState: string) => {
    const transitions = stateTransitions[currentState] || {};
    return {
      nextState: transitions.next,
      previousState: transitions.previous
    };
  };

  const formatDate = (dateString: string | Date | undefined) => {
    if (!dateString) return '-';
    const date = typeof dateString === 'string' ? new Date(dateString) : dateString;
    return date.toLocaleDateString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
      <div className="relative top-4 mx-auto p-5 border w-11/12 max-w-6xl shadow-lg rounded-md bg-white mb-8">
        {/* Header */}
        <div className="flex items-center justify-between mb-6">
          <div className="flex items-center gap-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                Detail transportního boxu
              </h2>
              {boxData?.transportBox && (
                <p className="text-gray-600">
                  {boxData.transportBox.code || `Box #${boxData.transportBox.id}`}
                </p>
              )}
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
            <span className="ml-2 text-gray-600">Načítám detail boxu...</span>
          </div>
        ) : error ? (
          <div className="flex items-center gap-2 text-red-600 py-8">
            <AlertCircle className="h-5 w-5" />
            <span>Chyba při načítání detailu boxu</span>
          </div>
        ) : boxData?.transportBox ? (
          <div className="space-y-6">
            {/* Basic Information */}
            <div className="bg-gray-50 p-4 rounded-lg">
              <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center gap-2">
                <Box className="h-5 w-5" />
                Základní informace
              </h3>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">ID</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.id}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Kód</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.code || '-'}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Stav</label>
                  <span className={`mt-1 inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                    stateColors[boxData.transportBox.state || ''] || 'bg-gray-100 text-gray-800'
                  }`}>
                    {stateLabels[boxData.transportBox.state || ''] || boxData.transportBox.state}
                  </span>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Lokace</label>
                  <p className="mt-1 text-sm text-gray-900 flex items-center gap-1">
                    <MapPin className="h-4 w-4 text-gray-400" />
                    {boxData.transportBox.location || '-'}
                  </p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Počet položek</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.itemCount}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Poslední změna</label>
                  <p className="mt-1 text-sm text-gray-900 flex items-center gap-1">
                    <Calendar className="h-4 w-4 text-gray-400" />
                    {formatDate(boxData.transportBox.lastStateChanged)}
                  </p>
                </div>
              </div>
              {boxData.transportBox.description && (
                <div className="mt-4">
                  <label className="block text-sm font-medium text-gray-700">Popis</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.description}</p>
                </div>
              )}
            </div>

            {/* Tab Navigation */}
            <div className="bg-white border border-gray-200 rounded-lg">
              <div className="border-b border-gray-200">
                <nav className="-mb-px flex space-x-8 px-4" aria-label="Tabs">
                  <button
                    onClick={() => setActiveTab('items')}
                    className={`whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm flex items-center gap-2 ${
                      activeTab === 'items'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <Tag className="h-4 w-4" />
                    Položky ({boxData.transportBox.items?.length || 0})
                  </button>
                  <button
                    onClick={() => setActiveTab('history')}
                    className={`whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm flex items-center gap-2 ${
                      activeTab === 'history'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <Clock className="h-4 w-4" />
                    Historie ({boxData.transportBox.stateLog?.length || 0})
                  </button>
                </nav>
              </div>
              
              <div className="p-0">
                {activeTab === 'items' && (
                  <div>
                    {boxData.transportBox.items && boxData.transportBox.items.length > 0 ? (
                      <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Kód produktu
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Název produktu
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Množství
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Přidáno
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Přidal
                              </th>
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {boxData.transportBox.items.map((item) => (
                              <tr key={item.id} className="hover:bg-gray-50">
                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                  {item.productCode}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  {item.productName || '-'}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  {item.amount}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  <div className="flex items-center gap-1">
                                    <Calendar className="h-4 w-4 text-gray-400" />
                                    {formatDate(item.dateAdded)}
                                  </div>
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  <div className="flex items-center gap-1">
                                    <User className="h-4 w-4 text-gray-400" />
                                    {item.userAdded || '-'}
                                  </div>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <div className="text-center py-8">
                        <Tag className="mx-auto h-12 w-12 text-gray-400" />
                        <h3 className="mt-2 text-sm font-medium text-gray-900">Žádné položky</h3>
                        <p className="mt-1 text-sm text-gray-500">
                          Tento transportní box neobsahuje žádné položky.
                        </p>
                      </div>
                    )}
                  </div>
                )}
                
                {activeTab === 'history' && (
                  <div>
                    {boxData.transportBox.stateLog && boxData.transportBox.stateLog.length > 0 ? (
                      <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Datum
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Stav
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Uživatel
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Popis
                              </th>
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {boxData.transportBox.stateLog.map((log) => (
                              <tr key={log.id} className="hover:bg-gray-50">
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  <div className="flex items-center gap-1">
                                    <Calendar className="h-4 w-4 text-gray-400" />
                                    {formatDate(log.stateDate)}
                                  </div>
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap">
                                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                                    stateColors[log.state || ''] || 'bg-gray-100 text-gray-800'
                                  }`}>
                                    {stateLabels[log.state || ''] || log.state}
                                  </span>
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  <div className="flex items-center gap-1">
                                    <User className="h-4 w-4 text-gray-400" />
                                    {log.user || '-'}
                                  </div>
                                </td>
                                <td className="px-6 py-4 text-sm text-gray-900">
                                  {log.description || '-'}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <div className="text-center py-8">
                        <Clock className="mx-auto h-12 w-12 text-gray-400" />
                        <h3 className="mt-2 text-sm font-medium text-gray-900">Žádná historie</h3>
                        <p className="mt-1 text-sm text-gray-500">
                          Pro tento transportní box není k dispozici historie stavů.
                        </p>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>
        ) : (
          <div className="text-center py-8">
            <Package className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">Box nenalezen</h3>
            <p className="mt-1 text-sm text-gray-500">
              Transportní box s ID {boxId} nebyl nalezen.
            </p>
          </div>
        )}

        {/* Footer with State Transition Buttons */}
        <div className="flex items-center justify-between pt-6 border-t border-gray-200">
          {/* State Transition Buttons */}
          {boxData?.transportBox && (() => {
            const { previousState, nextState } = getAvailableTransitions(boxData.transportBox.state || '');
            return (
              <div className="flex items-center gap-3">
                {/* Previous State Button - Left */}
                {previousState && (
                  <button
                    onClick={() => handleStateChange(previousState)}
                    disabled={changeStateMutation.isPending}
                    className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    title={`Změnit na: ${stateLabels[previousState] || previousState}`}
                  >
                    <ArrowLeft className="h-4 w-4 mr-2" />
                    {changeStateMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-1 animate-spin" />
                    ) : null}
                    {stateLabels[previousState] || previousState}
                  </button>
                )}
                
                {/* Current State Display */}
                <div className="flex items-center px-4 py-2 bg-gray-50 rounded-md border-2 border-gray-200">
                  <span className="text-sm text-gray-600 mr-2">Aktuální:</span>
                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                    stateColors[boxData.transportBox.state || ''] || 'bg-gray-100 text-gray-800'
                  }`}>
                    {stateLabels[boxData.transportBox.state || ''] || boxData.transportBox.state}
                  </span>
                </div>
                
                {/* Next State Button - Right */}
                {nextState && (
                  <button
                    onClick={() => handleStateChange(nextState)}
                    disabled={changeStateMutation.isPending}
                    className="flex items-center px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                    title={`Změnit na: ${stateLabels[nextState] || nextState}`}
                  >
                    {changeStateMutation.isPending ? (
                      <Loader2 className="h-4 w-4 mr-1 animate-spin" />
                    ) : null}
                    {stateLabels[nextState] || nextState}
                    <ArrowRight className="h-4 w-4 ml-2" />
                  </button>
                )}
              </div>
            );
          })()}
          
          {/* Close Button */}
          <button
            onClick={onClose}
            className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
          >
            Zavřít
          </button>
        </div>
      </div>
    </div>
  );
};

export default TransportBoxDetail;