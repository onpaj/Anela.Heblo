import React from 'react';
import { X, Package, Calendar, Truck, DollarSign, User, Clock, Loader2, AlertCircle, Edit, History } from 'lucide-react';
import { 
  usePurchaseOrderDetailQuery, 
  usePurchaseOrderHistoryQuery,
  useUpdatePurchaseOrderStatusMutation
} from '../../api/hooks/usePurchaseOrders';
import { UpdatePurchaseOrderStatusRequest } from '../../api/generated/api-client';

interface PurchaseOrderDetailProps {
  orderId: number;
  isOpen: boolean;
  onClose: () => void;
  onEdit?: (orderId: number) => void;
}

// Status labels and colors
const statusLabels: Record<string, string> = {
  'Draft': 'Návrh',
  'InTransit': 'V přepravě',
  'Completed': 'Dokončeno',
};

const statusColors: Record<string, string> = {
  'Draft': 'bg-gray-100 text-gray-800',
  'InTransit': 'bg-blue-100 text-blue-800',
  'Completed': 'bg-green-100 text-green-800',
};

const PurchaseOrderDetail: React.FC<PurchaseOrderDetailProps> = ({ orderId, isOpen, onClose, onEdit }) => {
  
  // Fetch order details and history
  const { data: orderData, isLoading: orderLoading, error: orderError } = usePurchaseOrderDetailQuery(orderId);
  const { data: historyData, isLoading: historyLoading } = usePurchaseOrderHistoryQuery(orderId);
  
  // Status update mutation
  const updateStatusMutation = useUpdatePurchaseOrderStatusMutation();

  // Add keyboard event listener for Esc key
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleKeyDown);
    }

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen, onClose]);

  if (!isOpen) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleStatusChange = async (newStatus: string) => {
    if (!orderData) return;
    
    try {
      await updateStatusMutation.mutateAsync({
        id: orderId,
        request: new UpdatePurchaseOrderStatusRequest({
          id: orderId,
          status: newStatus
        })
      });
    } catch (error) {
      console.error('Failed to update status:', error);
    }
  };

  // Format date for display
  const formatDate = (date: Date | string) => {
    const dateObj = typeof date === 'string' ? new Date(date) : date;
    return dateObj.toLocaleDateString('cs-CZ', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  };

  // Format currency
  const formatCurrency = (amount: number) => {
    return `${amount.toLocaleString('cs-CZ', { minimumFractionDigits: 2 })} Kč`;
  };

  // Format datetime
  const formatDateTime = (date: Date | string) => {
    const dateObj = typeof date === 'string' ? new Date(date) : date;
    return dateObj.toLocaleString('cs-CZ');
  };

  // Get next available status
  const getNextStatus = (currentStatus: string) => {
    switch (currentStatus) {
      case 'Draft':
        return 'InTransit';
      case 'InTransit':
        return 'Completed';
      default:
        return null;
    }
  };

  const getNextStatusLabel = (currentStatus: string) => {
    const nextStatus = getNextStatus(currentStatus);
    return nextStatus ? statusLabels[nextStatus] : null;
  };

  return (
    <div 
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-7xl w-full max-h-[90vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200 flex-shrink-0">
          <div className="flex items-center space-x-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                {orderData ? `Objednávka ${orderData.orderNumber}` : 'Načítání...'}
              </h2>
              <p className="text-sm text-gray-500">ID: {orderId}</p>
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
        <div className="flex-1 overflow-y-auto p-6 min-h-0">
          {orderLoading ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2">
                <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                <div className="text-gray-500">Načítání detailů objednávky...</div>
              </div>
            </div>
          ) : orderError ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2 text-red-600">
                <AlertCircle className="h-5 w-5" />
                <div>Chyba při načítání detailů: {orderError.message}</div>
              </div>
            </div>
          ) : orderData ? (
            <>
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 h-full">
                {/* Left Column - Order Information */}
                <div className="space-y-6 overflow-y-auto">
                  {/* Basic Information */}
                  <div className="space-y-4">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <Package className="h-5 w-5 mr-2 text-gray-500" />
                      Základní informace
                    </h3>
                    
                    <div className="bg-gray-50 rounded-lg p-4 space-y-3">
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600">Stav:</span>
                        <div className="flex items-center gap-2">
                          <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${orderData.status && statusColors[orderData.status] || 'bg-gray-100 text-gray-800'}`}>
                            {orderData.status && statusLabels[orderData.status] || orderData.status}
                          </span>
                          {orderData.status && getNextStatus(orderData.status) && (
                            <button
                              onClick={() => handleStatusChange(getNextStatus(orderData.status!)!)}
                              disabled={updateStatusMutation.isPending}
                              className="text-xs bg-indigo-600 hover:bg-indigo-700 text-white px-2 py-1 rounded disabled:opacity-50"
                            >
                              → {getNextStatusLabel(orderData.status!)}
                            </button>
                          )}
                        </div>
                      </div>
                      
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600 flex items-center">
                          <Truck className="h-4 w-4 mr-1" />
                          Dodavatel:
                        </span>
                        <span className="text-sm text-gray-900">{orderData.supplierName}</span>
                      </div>
                      
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600 flex items-center">
                          <Calendar className="h-4 w-4 mr-1" />
                          Datum objednávky:
                        </span>
                        <span className="text-sm text-gray-900">{orderData.orderDate ? formatDate(orderData.orderDate) : '-'}</span>
                      </div>
                      
                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600 flex items-center">
                          <Calendar className="h-4 w-4 mr-1" />
                          Plánované dodání:
                        </span>
                        <span className="text-sm text-gray-900">
                          {orderData.expectedDeliveryDate ? formatDate(orderData.expectedDeliveryDate) : 'Neurčeno'}
                        </span>
                      </div>

                      <div className="flex justify-between items-center">
                        <span className="text-sm font-medium text-gray-600 flex items-center">
                          <DollarSign className="h-4 w-4 mr-1" />
                          Celková částka:
                        </span>
                        <span className="text-lg font-semibold text-gray-900">{formatCurrency(orderData.totalAmount || 0)}</span>
                      </div>
                    </div>
                  </div>

                  {/* Notes */}
                  {orderData.notes && (
                    <div className="space-y-3">
                      <h3 className="text-lg font-medium text-gray-900">Poznámky</h3>
                      <div className="bg-gray-50 rounded-lg p-4">
                        <p className="text-sm text-gray-700">{orderData.notes}</p>
                      </div>
                    </div>
                  )}

                  {/* Metadata */}
                  <div className="space-y-3">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <User className="h-5 w-5 mr-2 text-gray-500" />
                      Metadata
                    </h3>
                    
                    <div className="bg-gray-50 rounded-lg p-4 space-y-2 text-sm">
                      <div className="flex justify-between">
                        <span className="text-gray-600">Vytvořil:</span>
                        <span className="font-medium">{orderData.createdBy}</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-gray-600">Datum vytvoření:</span>
                        <span className="font-medium">{orderData.createdAt ? formatDateTime(orderData.createdAt) : '-'}</span>
                      </div>
                      {orderData.updatedBy && (
                        <>
                          <div className="flex justify-between">
                            <span className="text-gray-600">Naposledy upravil:</span>
                            <span className="font-medium">{orderData.updatedBy}</span>
                          </div>
                          <div className="flex justify-between">
                            <span className="text-gray-600">Datum úpravy:</span>
                            <span className="font-medium">{formatDateTime(orderData.updatedAt!)}</span>
                          </div>
                        </>
                      )}
                    </div>
                  </div>
                </div>

                {/* Right Column - Order Lines and History */}
                <div className="space-y-6">
                  {/* Order Lines */}
                  <div className="space-y-4">
                    <h3 className="text-lg font-medium text-gray-900">Položky objednávky</h3>
                    
                    <div className="bg-gray-50 rounded-lg overflow-hidden">
                      <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-100">
                          <tr>
                            <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                              Materiál
                            </th>
                            <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase">
                              Množství
                            </th>
                            <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase">
                              Jedn. cena
                            </th>
                            <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase">
                              Celkem
                            </th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200 bg-white">
                          {orderData.lines?.map((line, index) => (
                            <tr key={index}>
                              <td className="px-4 py-2 text-sm text-gray-900">
                                {line.name || line.code || line.materialId}
                                {line.notes && (
                                  <div className="text-xs text-gray-500 mt-1">{line.notes}</div>
                                )}
                              </td>
                              <td className="px-4 py-2 text-sm text-gray-900 text-right">
                                {line.quantity}
                              </td>
                              <td className="px-4 py-2 text-sm text-gray-900 text-right">
                                {formatCurrency(line.unitPrice || 0)}
                              </td>
                              <td className="px-4 py-2 text-sm text-gray-900 text-right font-medium">
                                {formatCurrency(line.lineTotal || 0)}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>

                  {/* Order History */}
                  <div className="space-y-4">
                    <h3 className="text-lg font-medium text-gray-900 flex items-center">
                      <History className="h-5 w-5 mr-2 text-gray-500" />
                      Historie změn
                    </h3>
                    
                    <div className="bg-gray-50 rounded-lg p-4 max-h-64 overflow-y-auto">
                      {historyLoading ? (
                        <div className="flex items-center justify-center py-4">
                          <Loader2 className="h-4 w-4 animate-spin text-gray-500 mr-2" />
                          <span className="text-sm text-gray-500">Načítání historie...</span>
                        </div>
                      ) : historyData && historyData.length > 0 ? (
                        <div className="space-y-3">
                          {historyData.map((entry, index) => (
                            <div key={index} className="border-l-2 border-indigo-200 pl-4 pb-2">
                              <div className="flex items-center justify-between">
                                <span className="text-sm font-medium text-gray-900">{entry.action}</span>
                                <span className="text-xs text-gray-500">{entry.changedAt ? formatDateTime(entry.changedAt) : '-'}</span>
                              </div>
                              {entry.oldValue && entry.newValue && (
                                <div className="text-xs text-gray-600 mt-1">
                                  {entry.oldValue} → {entry.newValue}
                                </div>
                              )}
                              <div className="text-xs text-gray-500 mt-1">
                                <Clock className="h-3 w-3 inline mr-1" />
                                {entry.changedBy}
                              </div>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="text-center text-gray-500 py-4">
                          <span className="text-sm">Žádná historie změn</span>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </>
          ) : null}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-3 p-6 border-t border-gray-200 bg-gray-50 flex-shrink-0">
          <button
            onClick={onClose}
            className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200"
          >
            Zavřít
          </button>
          {orderData && orderData.status === 'Draft' && onEdit && (
            <button 
              onClick={() => onEdit(orderId)}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 flex items-center gap-2"
            >
              <Edit className="h-4 w-4" />
              Upravit
            </button>
          )}
        </div>
      </div>
    </div>
  );
};

export default PurchaseOrderDetail;