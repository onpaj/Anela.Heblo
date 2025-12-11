import React, { useState } from "react";
import { X, AlertCircle, CheckCircle, Clock, FileText, User, Mail, Phone, MapPin, Download, Loader2, ChevronDown, ChevronUp } from "lucide-react";
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
  const [expandedSyncItems, setExpandedSyncItems] = useState<Set<string>>(new Set());
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

  const getSyncStatusIcon = (isSuccess: boolean) => {
    return isSuccess 
      ? <CheckCircle className="h-4 w-4 text-green-500" />
      : <AlertCircle className="h-4 w-4 text-red-500" />;
  };

  const toggleSyncItemExpansion = (syncId: string) => {
    setExpandedSyncItems(prev => {
      const newSet = new Set(prev);
      if (newSet.has(syncId)) {
        newSet.delete(syncId);
      } else {
        newSet.add(syncId);
      }
      return newSet;
    });
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-7xl w-full mx-4 max-h-[90vh] overflow-hidden">
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
            <div className="p-6">
              {/* Two-column layout: Basic info left, Import history right */}
              <div className="grid grid-cols-1 xl:grid-cols-2 gap-8 h-full">
                
                {/* Left column - Basic information */}
                <div className="space-y-6">
                  {/* Basic information */}
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

                {/* Right column - Import history */}
                <div className="space-y-4">
                  <h3 className="text-lg font-medium text-gray-900 border-b border-gray-200 pb-2">
                    Historie importu 
                    {data.invoice.syncHistory && data.invoice.syncHistory.length > 0 && (
                      <span className="text-sm font-normal text-gray-500 ml-2">
                        ({data.invoice.syncHistory.length} pokusů)
                      </span>
                    )}
                  </h3>
                  
                  {data.invoice.syncHistory && data.invoice.syncHistory.length > 0 ? (
                    <div className="space-y-3 max-h-[calc(90vh-300px)] overflow-y-auto">
                      {data.invoice.syncHistory
                        .sort((a, b) => new Date(b.syncTime).getTime() - new Date(a.syncTime).getTime())
                        .map((sync, index) => {
                          const isExpanded = expandedSyncItems.has(sync.id.toString());
                          const isLatest = index === 0;
                          
                          return (
                            <div 
                              key={sync.id} 
                              className={`border rounded-lg ${
                                sync.isSuccess 
                                  ? isLatest ? 'border-green-300 bg-green-50' : 'border-green-200 bg-green-25'
                                  : isLatest ? 'border-red-300 bg-red-50' : 'border-red-200 bg-red-25'
                              }`}
                            >
                              <div 
                                className="p-4 cursor-pointer hover:bg-gray-50 transition-colors"
                                onClick={() => toggleSyncItemExpansion(sync.id.toString())}
                              >
                                <div className="flex items-center justify-between">
                                  <div className="flex items-center gap-3">
                                    {getSyncStatusIcon(sync.isSuccess)}
                                    <div>
                                      <div className="flex items-center gap-2">
                                        <span className={`font-medium ${
                                          sync.isSuccess ? 'text-green-800' : 'text-red-800'
                                        }`}>
                                          {sync.isSuccess ? 'Import úspěšný' : 'Import neúspěšný'}
                                        </span>
                                        {isLatest && (
                                          <span className="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
                                            Nejnovější
                                          </span>
                                        )}
                                      </div>
                                      <p className="text-sm text-gray-500">
                                        {formatDateTime(sync.syncTime)}
                                      </p>
                                    </div>
                                  </div>
                                  <div className="flex items-center gap-2">
                                    {sync.error && (
                                      <span className="text-xs text-red-600 bg-red-100 px-2 py-1 rounded">
                                        Chyba
                                      </span>
                                    )}
                                    {isExpanded ? (
                                      <ChevronUp className="h-5 w-5 text-gray-400" />
                                    ) : (
                                      <ChevronDown className="h-5 w-5 text-gray-400" />
                                    )}
                                  </div>
                                </div>
                                
                                {/* Quick error preview when collapsed */}
                                {!isExpanded && sync.error && (
                                  <div className="mt-2 p-2 bg-red-100 rounded text-sm text-red-700">
                                    <strong>Chyba:</strong> {sync.error.message}
                                  </div>
                                )}
                              </div>
                              
                              {/* Expandable content */}
                              {isExpanded && (
                                <div className="border-t border-gray-200 p-4 space-y-3">
                                  {/* Error details */}
                                  {sync.error && (
                                    <div className="bg-red-50 border border-red-200 rounded-lg p-3">
                                      <h4 className="font-medium text-red-800 mb-2">
                                        Detail chyby pro tento pokus
                                      </h4>
                                      <div className="space-y-2 text-sm">
                                        <div>
                                          <span className="font-medium text-red-700">Zpráva:</span>
                                          <p className="text-red-600 mt-1">{sync.error.message}</p>
                                        </div>
                                        {sync.error.code && (
                                          <div>
                                            <span className="font-medium text-red-700">Kód chyby:</span>
                                            <p className="text-red-600 font-mono text-xs">{sync.error.code}</p>
                                          </div>
                                        )}
                                        {sync.error.field && (
                                          <div>
                                            <span className="font-medium text-red-700">Problematické pole:</span>
                                            <p className="text-red-600 font-mono text-xs">{sync.error.field}</p>
                                          </div>
                                        )}
                                        <div>
                                          <span className="font-medium text-red-700">Typ chyby:</span>
                                          <p className="text-red-600 text-xs">
                                            {sync.error.errorType === 0 ? 'Obecná chyba' : 
                                             sync.error.errorType === 1 ? 'Faktura již spárována' : 
                                             sync.error.errorType === 2 ? 'Produkt nenalezen' : 
                                             `Neznámý typ (${sync.error.errorType})`}
                                          </p>
                                        </div>
                                      </div>
                                    </div>
                                  )}

                                  {/* Current error state (if this is the latest attempt and there's a current error) */}
                                  {isLatest && data.invoice.errorType && data.invoice.errorMessage && (
                                    <div className="bg-orange-50 border border-orange-200 rounded-lg p-3">
                                      <h4 className="font-medium text-orange-800 mb-2">
                                        Aktuální stav synchronizace
                                      </h4>
                                      <div className="space-y-2 text-sm">
                                        <div>
                                          <span className="font-medium text-orange-700">Zpráva:</span>
                                          <p className="text-orange-600 mt-1">{data.invoice.errorMessage}</p>
                                        </div>
                                        <div>
                                          <span className="font-medium text-orange-700">Typ chyby:</span>
                                          <p className="text-orange-600 text-xs">
                                            {data.invoice.errorType === '0' ? 'Obecná chyba' : 
                                             data.invoice.errorType === '1' ? 'Faktura již spárována' : 
                                             data.invoice.errorType === '2' ? 'Produkt nenalezen' : 
                                             `Neznámý typ (${data.invoice.errorType})`}
                                          </p>
                                        </div>
                                      </div>
                                    </div>
                                  )}
                                  
                                  {/* Raw data from the import attempt */}
                                  {sync.data && (
                                    <div className="bg-gray-50 border border-gray-200 rounded-lg p-3">
                                      <details className="text-sm">
                                        <summary className="font-medium text-gray-700 cursor-pointer hover:text-gray-900 mb-2">
                                          Zobrazit raw data z tohoto pokusu
                                        </summary>
                                        <pre className="mt-2 p-2 bg-white border rounded text-xs overflow-x-auto whitespace-pre-wrap font-mono text-gray-600 max-h-40">
                                          {sync.data}
                                        </pre>
                                      </details>
                                    </div>
                                  )}

                                  {/* Synchronization result from external system */}
                                  {sync.data && (
                                    <div className="bg-blue-50 border border-blue-200 rounded-lg p-3">
                                      <details className="text-sm">
                                        <summary className="font-medium text-blue-700 cursor-pointer hover:text-blue-900 mb-2">
                                          Detail odpovědi ze systému ABRA Flexi
                                        </summary>
                                        <div className="mt-2 space-y-2">
                                          {(() => {
                                            try {
                                              const parsed = JSON.parse(sync.data);
                                              return (
                                                <div className="p-2 bg-white border rounded text-xs">
                                                  <div className="space-y-1">
                                                    {parsed.id && (
                                                      <div>
                                                        <span className="font-medium text-blue-700">ID faktury:</span>
                                                        <span className="ml-2 font-mono">{parsed.id}</span>
                                                      </div>
                                                    )}
                                                    {parsed.kod && (
                                                      <div>
                                                        <span className="font-medium text-blue-700">Kód faktury:</span>
                                                        <span className="ml-2 font-mono">{parsed.kod}</span>
                                                      </div>
                                                    )}
                                                    {parsed.datVyst && (
                                                      <div>
                                                        <span className="font-medium text-blue-700">Datum vystavení:</span>
                                                        <span className="ml-2">{parsed.datVyst}</span>
                                                      </div>
                                                    )}
                                                    {parsed.mena && (
                                                      <div>
                                                        <span className="font-medium text-blue-700">Měna:</span>
                                                        <span className="ml-2">{parsed.mena}</span>
                                                      </div>
                                                    )}
                                                    {parsed.nazFirmy && (
                                                      <div>
                                                        <span className="font-medium text-blue-700">Zákazník:</span>
                                                        <span className="ml-2">{parsed.nazFirmy}</span>
                                                      </div>
                                                    )}
                                                    {parsed.polozkyDokladu && Array.isArray(parsed.polozkyDokladu) && (
                                                      <div>
                                                        <span className="font-medium text-blue-700">Počet položek:</span>
                                                        <span className="ml-2">{parsed.polozkyDokladu.length}</span>
                                                      </div>
                                                    )}
                                                  </div>
                                                </div>
                                              );
                                            } catch {
                                              return (
                                                <p className="text-gray-600 text-xs p-2 bg-white border rounded">
                                                  Nepodařilo se parsovat data jako JSON
                                                </p>
                                              );
                                            }
                                          })()}
                                        </div>
                                      </details>
                                    </div>
                                  )}
                                  
                                  {/* Success state */}
                                  {sync.isSuccess && (
                                    <div className="bg-green-50 border border-green-200 rounded-lg p-3">
                                      <div className="flex items-center gap-2">
                                        <CheckCircle className="h-5 w-5 text-green-600" />
                                        <span className="font-medium text-green-800">
                                          Import byl dokončen úspěšně
                                        </span>
                                      </div>
                                      <p className="text-sm text-green-600 mt-1">
                                        Faktura byla úspěšně importována do systému ABRA Flexi.
                                      </p>
                                    </div>
                                  )}
                                </div>
                              )}
                            </div>
                          );
                        })}
                    </div>
                  ) : (
                    <div className="text-center py-8 text-gray-500">
                      <Clock className="h-12 w-12 mx-auto mb-4 text-gray-300" />
                      <p className="text-sm">Zatím žádná historie importu</p>
                      <p className="text-xs mt-1">Faktura ještě nebyla importována</p>
                    </div>
                  )}
                </div>
              </div>
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
                      Znovu importovat
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