import React, { useState } from 'react';
import { X, Package, Calendar, Truck, DollarSign, User, Clock, Loader2, AlertCircle, Edit, History, Phone, Receipt, FileCheck, Info, ScrollText, FileText, StickyNote } from 'lucide-react';
import { 
  usePurchaseOrderDetailQuery, 
  usePurchaseOrderHistoryQuery,
  useUpdatePurchaseOrderStatusMutation,
  useUpdatePurchaseOrderInvoiceAcquiredMutation
} from '../../api/hooks/usePurchaseOrders';
import { UpdatePurchaseOrderStatusRequest, UpdatePurchaseOrderInvoiceAcquiredRequest } from '../../api/generated/api-client';

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
  // Tab state
  const [activeTab, setActiveTab] = useState<'info' | 'log'>('info');
  
  // Fetch order details and history
  const { data: orderData, isLoading: orderLoading, error: orderError } = usePurchaseOrderDetailQuery(orderId);
  const { data: historyData, isLoading: historyLoading } = usePurchaseOrderHistoryQuery(orderId);
  
  // Status update mutation
  const updateStatusMutation = useUpdatePurchaseOrderStatusMutation();
  
  // Invoice acquired update mutation
  const updateInvoiceAcquiredMutation = useUpdatePurchaseOrderInvoiceAcquiredMutation();

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

  const handleInvoiceAcquiredToggle = async () => {
    if (!orderData) return;
    
    try {
      await updateInvoiceAcquiredMutation.mutateAsync({
        id: orderId,
        request: new UpdatePurchaseOrderInvoiceAcquiredRequest({
          id: orderId,
          invoiceAcquired: !orderData.invoiceAcquired
        })
      });
    } catch (error) {
      console.error('Failed to update invoice acquired:', error);
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
    return `${amount.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;
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
        <div className="flex-1 overflow-hidden flex flex-col min-h-0">
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
              {/* Tabs */}
              <div className="border-b border-gray-200 px-6 pt-6">
                <nav className="-mb-px flex space-x-8">
                  <button
                    onClick={() => setActiveTab('info')}
                    className={`${
                      activeTab === 'info'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <Info className="h-4 w-4 mr-2" />
                    Základní informace
                  </button>
                  <button
                    onClick={() => setActiveTab('log')}
                    className={`${
                      activeTab === 'log'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <ScrollText className="h-4 w-4 mr-2" />
                    Log
                  </button>
                </nav>
              </div>

              {/* Tab Content */}
              <div className="flex-1 overflow-hidden">
                {activeTab === 'info' ? (
                  <div className="p-6 h-full flex flex-col min-h-0">
                    <div className="space-y-6 flex-1 flex flex-col min-h-0">
                      {/* Top Section - Two Column Layout */}
                      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        {/* Left Column - Order Information */}
                        <div className="space-y-6">
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
                                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${(orderData.status && statusColors[orderData.status]) || 'bg-gray-100 text-gray-800'}`}>
                                    {(orderData.status && statusLabels[orderData.status]) || orderData.status}
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

                              {orderData.contactVia && (
                                <div className="flex justify-between items-center">
                                  <span className="text-sm font-medium text-gray-600 flex items-center">
                                    <Phone className="h-4 w-4 mr-1" />
                                    Způsob komunikace:
                                  </span>
                                  <span className="text-sm text-gray-900">
                                    {orderData.contactVia === 'Phone' ? 'Telefon' :
                                     orderData.contactVia === 'Email' ? 'Email' :
                                     orderData.contactVia === 'WhatsApp' ? 'WhatsApp' :
                                     orderData.contactVia === 'F2F' ? 'Osobně' :
                                     orderData.contactVia === 'Eshop' ? 'Eshop' :
                                     orderData.contactVia === 'Other' ? 'Jiné' :
                                     orderData.contactVia}
                                  </span>
                                </div>
                              )}

                              <div className="flex justify-between items-center">
                                <span className="text-sm font-medium text-gray-600 flex items-center">
                                  <Receipt className="h-4 w-4 mr-1" />
                                  Mám fakturu:
                                </span>
                                <div className="flex items-center gap-2">
                                  <span className={`text-sm ${orderData.invoiceAcquired ? 'text-green-600 font-medium' : 'text-gray-500'}`}>
                                    {orderData.invoiceAcquired ? 'Ano' : 'Ne'}
                                  </span>
                                  <button
                                    onClick={handleInvoiceAcquiredToggle}
                                    disabled={updateInvoiceAcquiredMutation.isPending}
                                    className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 ${
                                      orderData.invoiceAcquired ? 'bg-green-600' : 'bg-gray-300'
                                    } ${updateInvoiceAcquiredMutation.isPending ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
                                  >
                                    <span
                                      className={`inline-block h-3 w-3 transform rounded-full bg-white transition-transform ${
                                        orderData.invoiceAcquired ? 'translate-x-5' : 'translate-x-1'
                                      }`}
                                    />
                                    {orderData.invoiceAcquired && (
                                      <FileCheck className="absolute left-0.5 h-3 w-3 text-white" />
                                    )}
                                  </button>
                                </div>
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

                        </div>

                        {/* Right Column - Notes Section */}
                        <div className="space-y-4">
                          <h3 className="text-lg font-medium text-gray-900 flex items-center">
                            <FileText className="h-5 w-5 mr-2 text-gray-500" />
                            Poznámky
                          </h3>
                          
                          <div className="space-y-4">
                            <div>
                              <h4 className="text-sm font-medium text-gray-700 mb-2">Poznámka od dodavatele</h4>
                              <div className="bg-gray-50 rounded-lg p-4 min-h-[80px]">
                                {orderData.supplierNote ? (
                                  <pre className="whitespace-pre-wrap font-sans text-sm text-gray-700">
                                    {orderData.supplierNote}
                                  </pre>
                                ) : (
                                  <div className="flex items-center justify-center py-4">
                                    <span className="text-gray-400 italic text-sm">Žádná poznámka od dodavatele</span>
                                  </div>
                                )}
                              </div>
                            </div>

                            <div>
                              <h4 className="text-sm font-medium text-gray-700 mb-2">Poznámky k objednávce</h4>
                              <div className="bg-gray-50 rounded-lg p-4 min-h-[80px]">
                                {orderData.notes ? (
                                  <pre className="whitespace-pre-wrap font-sans text-sm text-gray-700">
                                    {orderData.notes}
                                  </pre>
                                ) : (
                                  <div className="flex items-center justify-center py-4">
                                    <span className="text-gray-400 italic text-sm">Žádné poznámky k objednávce</span>
                                  </div>
                                )}
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>

                      {/* Order Lines Section */}
                      <div className="space-y-4 flex-1 flex flex-col min-h-0">
                        <h3 className="text-lg font-medium text-gray-900">Položky objednávky</h3>
                        
                        <div className="bg-white rounded-lg shadow overflow-hidden flex-1 flex flex-col min-h-0 max-h-80">
                          <div className="overflow-auto flex-1">
                            <table className="min-w-full divide-y divide-gray-200">
                              <thead className="bg-gray-50 sticky top-0">
                                <tr>
                                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Materiál
                                  </th>
                                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Množství
                                  </th>
                                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Jedn. cena
                                  </th>
                                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Celkem
                                  </th>
                                </tr>
                              </thead>
                              <tbody className="bg-white divide-y divide-gray-200">
                                {orderData.lines?.map((line, index) => (
                                  <tr key={index} className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                      <div>
                                        <div className="font-medium flex items-center gap-2">
                                          {line.materialName || line.materialId}
                                          {line.catalogNote && (
                                            <div className="relative group">
                                              <StickyNote className="h-4 w-4 text-amber-500 cursor-help" />
                                              <div className="absolute z-10 invisible group-hover:visible bg-gray-800 text-white text-xs rounded py-1 px-2 -top-8 left-1/2 transform -translate-x-1/2 whitespace-nowrap">
                                                {line.catalogNote}
                                              </div>
                                            </div>
                                          )}
                                        </div>
                                        {line.notes && (
                                          <div className="text-xs text-gray-500 mt-1">{line.notes}</div>
                                        )}
                                      </div>
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                                      {line.quantity}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                                      {formatCurrency(line.unitPrice || 0)}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 text-right">
                                      {formatCurrency(line.lineTotal || 0)}
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                ) : (
                  /* Log Tab */
                  <div className="p-6 h-full overflow-y-auto">
                    <div className="space-y-6">
                      {/* Historie změn */}
                      <div className="space-y-4">
                        <h3 className="text-lg font-medium text-gray-900 flex items-center">
                          <History className="h-5 w-5 mr-2 text-gray-500" />
                          Historie změn
                        </h3>
                        
                        <div className="bg-gray-50 rounded-lg p-4">
                          {historyLoading ? (
                            <div className="flex items-center justify-center py-8">
                              <Loader2 className="h-4 w-4 animate-spin text-gray-500 mr-2" />
                              <span className="text-sm text-gray-500">Načítání historie...</span>
                            </div>
                          ) : historyData && historyData.length > 0 ? (
                            <div className="space-y-4">
                              {historyData.map((entry, index) => (
                                <div key={index} className="border-l-2 border-indigo-200 pl-4 pb-4">
                                  <div className="flex items-center justify-between mb-1">
                                    <span className="text-sm font-medium text-gray-900">{entry.action}</span>
                                    <span className="text-xs text-gray-500">{entry.changedAt ? formatDateTime(entry.changedAt) : '-'}</span>
                                  </div>
                                  {entry.oldValue && entry.newValue && (
                                    <div className="text-xs text-gray-600 mb-1">
                                      <span className="bg-red-100 px-1 rounded">{entry.oldValue}</span>
                                      <span className="mx-1">→</span>
                                      <span className="bg-green-100 px-1 rounded">{entry.newValue}</span>
                                    </div>
                                  )}
                                  <div className="text-xs text-gray-500 flex items-center">
                                    <Clock className="h-3 w-3 mr-1" />
                                    {entry.changedBy}
                                  </div>
                                </div>
                              ))}
                            </div>
                          ) : (
                            <div className="text-center text-gray-500 py-8">
                              <span className="text-sm">Žádná historie změn</span>
                            </div>
                          )}
                        </div>
                      </div>

                      {/* Metadata */}
                      <div className="space-y-4">
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
                  </div>
                )}
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
          {orderData && orderData.isEditable && onEdit && (
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