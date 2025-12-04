import React, { useState } from "react";
import { X, AlertCircle, CheckCircle, Clock, FileText, User, Mail, Phone, MapPin, Download, Loader2 } from "lucide-react";
import { useIssuedInvoiceDetail } from "../../api/hooks/useIssuedInvoices";
import { useEnqueueInvoiceImport } from "../../api/hooks/useAsyncInvoiceImport";
import { formatDate, formatDateTime, formatCurrency } from "../../utils/formatters";

interface IssuedInvoiceDetailModalProps {
  invoiceId: string;
  isOpen: boolean;
  onClose: () => void;
  onInvoiceUpdated?: () => Promise<void>;
  onJobStarted?: (jobId: string) => void;
}

const IssuedInvoiceDetailModal: React.FC<IssuedInvoiceDetailModalProps> = ({
  invoiceId,
  isOpen,
  onClose,
  onInvoiceUpdated,
  onJobStarted,
}) => {
  const { data, isLoading, error } = useIssuedInvoiceDetail(invoiceId);
  const [reimporting, setReimporting] = useState(false);
  const enqueueImportMutation = useEnqueueInvoiceImport();

  if (!isOpen) return null;

  const handleReimport = async (currency?: 'CZK' | 'EUR') => {
    // Use invoice's existing currency as default if not specified
    const targetCurrency = currency || (data?.invoice as any)?.currency || 'CZK';
    if (reimporting) return;

    try {
      setReimporting(true);

      const requestBody = {
        query: {
          requestId: `reimport-${invoiceId}-${Date.now()}`,
          invoiceId: invoiceId,
          currency: targetCurrency
        }
      };

      // Use async import
      const result = await enqueueImportMutation.mutateAsync(requestBody);

      // Pass jobId to parent and close modal
      if (result.jobId && onJobStarted) {
        onJobStarted(result.jobId);
      }
      
      // Close modal immediately
      onClose();

      // Refresh data in background
      if (onInvoiceUpdated) {
        onInvoiceUpdated();
      }

    } catch (error) {
      console.error('Reimport error:', error);
    } finally {
      setReimporting(false);
    }
  };

  const getSyncStatusBadge = (isSynced: boolean, errorType?: string | null) => {
    if (errorType) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
          <AlertCircle className="h-3 w-3 mr-1" />
          Chyba při synchronizaci
        </span>
      );
    }
    if (isSynced) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
          <CheckCircle className="h-3 w-3 mr-1" />
          Synchronizováno
        </span>
      );
    }
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
        <Clock className="h-3 w-3 mr-1" />
        Čeká na synchronizaci
      </span>
    );
  };

  const getSyncStatusIcon = (status: string | null | undefined) => {
    if (!status) {
      return <Clock className="h-4 w-4 text-yellow-500" />;
    }
    
    switch (status.toLowerCase()) {
      case 'success':
        return <CheckCircle className="h-4 w-4 text-green-500" />;
      case 'error':
      case 'failed':
        return <AlertCircle className="h-4 w-4 text-red-500" />;
      default:
        return <Clock className="h-4 w-4 text-yellow-500" />;
    }
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full mx-4 max-h-[90vh] overflow-hidden">
        {/* Header */}
        <div className="flex justify-between items-center p-6 border-b border-gray-200">
          <div className="flex items-center">
            <FileText className="h-6 w-6 text-gray-400 mr-3" />
            <h2 className="text-xl font-semibold text-gray-900">
              Detail faktury {invoiceId}
            </h2>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors p-1"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <div className="overflow-y-auto max-h-[calc(90vh-80px)]">
          {/* Loading state */}
          {isLoading && (
            <div className="p-6 text-center">
              <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600 mx-auto"></div>
              <p className="text-gray-500 mt-4">Načítání detailu faktury...</p>
            </div>
          )}

          {/* Error state */}
          {error && (
            <div className="p-6 text-center">
              <div className="text-red-600">
                <AlertCircle className="h-12 w-12 mx-auto mb-4" />
                <p className="font-medium">Chyba při načítání detailu faktury</p>
                <p className="text-sm mt-1">{error.message}</p>
              </div>
            </div>
          )}

          {/* Invoice detail */}
          {data && data.invoice && (
            <div className="p-6 space-y-6">
              {/* Basic information */}
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <div className="space-y-4">
                  <h3 className="text-lg font-medium text-gray-900 border-b border-gray-200 pb-2">
                    Základní informace
                  </h3>
                  
                  <div className="space-y-3">
                    <div>
                      <label className="text-sm font-medium text-gray-500">Číslo faktury</label>
                      <p className="text-sm text-gray-900">{data.invoice.id}</p>
                    </div>
                    
                    <div>
                      <label className="text-sm font-medium text-gray-500">Datum faktury</label>
                      <p className="text-sm text-gray-900">{formatDate(data.invoice.invoiceDate)}</p>
                    </div>
                    
                    <div>
                      <label className="text-sm font-medium text-gray-500">Celková částka</label>
                      <p className="text-sm text-gray-900 font-semibold">
                        {formatCurrency(data.invoice.price)}
                        <span className={`ml-2 inline-flex items-center px-2 py-1 rounded text-xs font-medium ${
                          (data.invoice as any).currency === 'EUR' 
                            ? 'bg-yellow-100 text-yellow-800' 
                            : 'bg-blue-100 text-blue-800'
                        }`}>
                          {(data.invoice as any).currency || 'CZK'}
                        </span>
                      </p>
                    </div>
                    
                    <div>
                      <label className="text-sm font-medium text-gray-500">Stav synchronizace</label>
                      <div className="mt-1">
                        {getSyncStatusBadge(data.invoice.isSynced, data.invoice.errorType)}
                      </div>
                    </div>
                    
                    {data.invoice.lastSyncTime && (
                      <div>
                        <label className="text-sm font-medium text-gray-500">Poslední synchronizace</label>
                        <p className="text-sm text-gray-900">{formatDateTime(data.invoice.lastSyncTime)}</p>
                      </div>
                    )}
                  </div>
                </div>

                {/* Customer information */}
                <div className="space-y-4">
                  <h3 className="text-lg font-medium text-gray-900 border-b border-gray-200 pb-2">
                    Informace o zákazníkovi
                  </h3>
                  
                  <div className="space-y-3">
                    {data.invoice.customerName && (
                      <div className="flex items-center">
                        <User className="h-4 w-4 text-gray-400 mr-2 flex-shrink-0" />
                        <div>
                          <label className="text-sm font-medium text-gray-500">Jméno</label>
                          <p className="text-sm text-gray-900">{data.invoice.customerName}</p>
                        </div>
                      </div>
                    )}
                    
                    {data.invoice.customerEmail && (
                      <div className="flex items-center">
                        <Mail className="h-4 w-4 text-gray-400 mr-2 flex-shrink-0" />
                        <div>
                          <label className="text-sm font-medium text-gray-500">E-mail</label>
                          <p className="text-sm text-gray-900">{data.invoice.customerEmail}</p>
                        </div>
                      </div>
                    )}
                    
                    {data.invoice.customerPhone && (
                      <div className="flex items-center">
                        <Phone className="h-4 w-4 text-gray-400 mr-2 flex-shrink-0" />
                        <div>
                          <label className="text-sm font-medium text-gray-500">Telefon</label>
                          <p className="text-sm text-gray-900">{data.invoice.customerPhone}</p>
                        </div>
                      </div>
                    )}
                    
                    {data.invoice.customerAddress && (
                      <div className="flex items-start">
                        <MapPin className="h-4 w-4 text-gray-400 mr-2 flex-shrink-0 mt-0.5" />
                        <div>
                          <label className="text-sm font-medium text-gray-500">Adresa</label>
                          <p className="text-sm text-gray-900 whitespace-pre-line">{data.invoice.customerAddress}</p>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>

              {/* Items */}
              {data.invoice.items && data.invoice.items.length > 0 && (
                <div>
                  <h3 className="text-lg font-medium text-gray-900 border-b border-gray-200 pb-2 mb-4">
                    Položky faktury
                  </h3>
                  
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-gray-50">
                        <tr>
                          <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">
                            Produkt
                          </th>
                          <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase">
                            Množství
                          </th>
                          <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase">
                            Jednotková cena
                          </th>
                          <th className="px-4 py-2 text-right text-xs font-medium text-gray-500 uppercase">
                            Celkem
                          </th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-200">
                        {data.invoice.items.map((item, index) => (
                          <tr key={index}>
                            <td className="px-4 py-2">
                              <div>
                                <p className="font-medium text-gray-900">{item.productName}</p>
                                <p className="text-gray-500 text-xs">{item.productId}</p>
                              </div>
                            </td>
                            <td className="px-4 py-2 text-right text-gray-900">{item.quantity}</td>
                            <td className="px-4 py-2 text-right text-gray-900">{formatCurrency(item.unitPrice)}</td>
                            <td className="px-4 py-2 text-right text-gray-900 font-medium">{formatCurrency(item.totalPrice)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Sync history */}
              {data.invoice.syncHistory && data.invoice.syncHistory.length > 0 && (
                <div>
                  <h3 className="text-lg font-medium text-gray-900 border-b border-gray-200 pb-2 mb-4">
                    Historie synchronizace
                  </h3>
                  
                  <div className="space-y-3">
                    {data.invoice.syncHistory.map((sync) => (
                      <div key={sync.id} className="border border-gray-200 rounded-lg p-4">
                        <div className="flex items-center justify-between mb-2">
                          <div className="flex items-center">
                            {getSyncStatusIcon(sync.syncStatus)}
                            <span className="ml-2 font-medium text-gray-900 capitalize">
                              {sync.syncStatus}
                            </span>
                          </div>
                          <span className="text-sm text-gray-500">
                            {formatDateTime(sync.syncTime)}
                          </span>
                        </div>
                        
                        {sync.errorMessage && (
                          <div className="mt-2">
                            <p className="text-sm text-red-600">{sync.errorMessage}</p>
                          </div>
                        )}
                        
                        {sync.responseData && (
                          <div className="mt-2">
                            <details className="text-sm">
                              <summary className="text-gray-500 cursor-pointer hover:text-gray-700">
                                Zobrazit odpověď serveru
                              </summary>
                              <pre className="mt-2 p-2 bg-gray-100 rounded text-xs overflow-x-auto whitespace-pre-wrap">
                                {sync.responseData}
                              </pre>
                            </details>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Error details */}
              {data.invoice.errorType && data.invoice.errorMessage && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4">
                  <h3 className="text-lg font-medium text-red-800 mb-2">
                    Chyba synchronizace
                  </h3>
                  <p className="text-sm text-red-600">{data.invoice.errorMessage}</p>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-gray-200 px-6 py-4 bg-gray-50">
          <div className="flex justify-between items-center">
            <div className="flex items-center gap-3">
              {data && data.invoice && (
                <button
                  onClick={() => handleReimport()}
                  disabled={reimporting}
                  className="px-4 py-2 text-sm font-medium text-white bg-emerald-600 border border-transparent rounded-md hover:bg-emerald-700 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:ring-offset-2 disabled:opacity-50 flex items-center gap-2"
                >
                  {reimporting ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Importuje...
                    </>
                  ) : (
                    <>
                      <Download className="h-4 w-4" />
                      Znovu naimportovat
                    </>
                  )}
                </button>
              )}
            </div>
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
            >
              Zavřít
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default IssuedInvoiceDetailModal;