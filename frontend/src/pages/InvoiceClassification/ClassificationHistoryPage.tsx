import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, Calendar, ChevronLeft, ChevronRight, AlertCircle, CheckCircle2, Clock, Play, Plus, Filter } from 'lucide-react';
import { format } from 'date-fns';
import { cs } from 'date-fns/locale';
import { useClassificationHistory, ClassificationHistoryItem, useClassifySingleInvoice, useCreateClassificationRule, CreateClassificationRuleRequest } from '../../api/hooks/useInvoiceClassification';
import RuleForm from './components/RuleForm';

const ClassificationHistoryPage: React.FC = () => {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [fromDate, setFromDate] = useState<Date | undefined>();
  const [toDate, setToDate] = useState<Date | undefined>();
  const [invoiceNumber, setInvoiceNumber] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [classifyingInvoiceId, setClassifyingInvoiceId] = useState<string | null>(null);
  const [showRuleModal, setShowRuleModal] = useState(false);
  const [prefillCompanyName, setPrefillCompanyName] = useState('');

  const { data: historyData, isLoading, error, refetch } = useClassificationHistory(
    page,
    pageSize,
    fromDate,
    toDate,
    invoiceNumber || undefined,
    companyName || undefined
  );

  const classifySingleInvoiceMutation = useClassifySingleInvoice();
  const createRuleMutation = useCreateClassificationRule();

  const getResultBadge = (result: ClassificationHistoryItem['result']) => {
    switch (result) {
      case 'Success':
        return (
          <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 text-emerald-800">
            <CheckCircle2 className="w-3 h-3 mr-1" />
            Úspěch
          </span>
        );
      case 'ManualReviewRequired':
        return (
          <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
            <Clock className="w-3 h-3 mr-1" />
            Ruční kontrola
          </span>
        );
      case 'Error':
        return (
          <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
            <AlertCircle className="w-3 h-3 mr-1" />
            Chyba
          </span>
        );
      default:
        return null;
    }
  };

  const formatDate = (date?: Date | string) => {
    if (!date) return '-';
    try {
      const dateObj = date instanceof Date ? date : new Date(date);
      return format(dateObj, 'dd.MM.yyyy HH:mm', { locale: cs });
    } catch {
      return typeof date === 'string' ? date : '-';
    }
  };

  const formatInvoiceDate = (date?: Date | string) => {
    if (!date) return '-';
    try {
      const dateObj = date instanceof Date ? date : new Date(date);
      return format(dateObj, 'dd.MM.yyyy', { locale: cs });
    } catch {
      return typeof date === 'string' ? date : '-';
    }
  };

  const handleSearch = () => {
    setPage(1); // Reset to first page when searching
    refetch(); // Refresh data with current filters
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPage(1); // Reset to first page when changing page size
  };

  const clearFilters = () => {
    setFromDate(undefined);
    setToDate(undefined);
    setInvoiceNumber('');
    setCompanyName('');
    setPage(1);
    // Refetch data after clearing filters
    setTimeout(() => refetch(), 0);
  };


  const handleClassifyInvoice = async (invoiceId: string) => {
    try {
      setClassifyingInvoiceId(invoiceId);
      await classifySingleInvoiceMutation.mutateAsync(invoiceId);
      // Success message could be added here
    } catch (error) {
      console.error('Error classifying invoice:', error);
      // Error handling could be improved here
    } finally {
      setClassifyingInvoiceId(null);
    }
  };

  const handleCreateRule = (companyName: string) => {
    setPrefillCompanyName(companyName);
    setShowRuleModal(true);
  };

  const handleRuleSubmit = async (data: CreateClassificationRuleRequest) => {
    try {
      await createRuleMutation.mutateAsync(data);
      setShowRuleModal(false);
      setPrefillCompanyName('');
    } catch (error) {
      console.error('Error creating rule:', error);
      // Close modal even on error to prevent UI issues
      setShowRuleModal(false);
      setPrefillCompanyName('');
      // Error handling could be improved here
    }
  };

  const handleRuleCancel = () => {
    setShowRuleModal(false);
    setPrefillCompanyName('');
  };

  if (error) {
    return (
      <div className="p-6">
        <div className="bg-red-50 border border-red-200 rounded-md p-4">
          <div className="flex">
            <AlertCircle className="h-5 w-5 text-red-400" />
            <div className="ml-3">
              <h3 className="text-sm font-medium text-red-800">
                Chyba při načítání historie klasifikace
              </h3>
              <div className="mt-2 text-sm text-red-700">
                {error instanceof Error ? error.message : 'Unknown error occurred'}
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col p-6 space-y-6">

      {/* Filters */}
      <div className="bg-white shadow rounded-lg p-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <Calendar className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                <input
                  type="date"
                  id="fromDate"
                  value={fromDate ? format(fromDate, 'yyyy-MM-dd') : ''}
                  onChange={(e) => setFromDate(e.target.value ? new Date(e.target.value) : undefined)}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Od data..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <Calendar className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                <input
                  type="date"
                  id="toDate"
                  value={toDate ? format(toDate, 'yyyy-MM-dd') : ''}
                  onChange={(e) => setToDate(e.target.value ? new Date(e.target.value) : undefined)}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Do data..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                <input
                  type="text"
                  id="invoiceNumber"
                  value={invoiceNumber}
                  onChange={(e) => setInvoiceNumber(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Číslo faktury..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                <input
                  type="text"
                  id="companyName"
                  value={companyName}
                  onChange={(e) => setCompanyName(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Název firmy..."
                />
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleSearch}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
            >
              Filtrovat
            </button>
            <button
              onClick={clearFilters}
              className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
            >
              Vymazat
            </button>
          </div>
        </div>
      </div>

      {/* Results */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col">
        {isLoading ? (
          <div className="p-8 text-center">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mx-auto"></div>
            <p className="mt-2 text-sm text-gray-500">Načítání...</p>
          </div>
        ) : historyData?.items?.length === 0 ? (
          <div className="p-8 text-center">
            <p className="text-gray-500">Nebyly nalezeny žádné záznamy klasifikace</p>
          </div>
        ) : (
          <>
            {/* Table */}
            <div className="flex-1 overflow-x-auto overflow-y-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Faktura
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Firma
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Popis
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Pravidlo
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Předpis
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Oddělení
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Výsledek
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Klasifikováno
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Akce
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {historyData?.items?.map((item) => (
                    <tr key={item.id} className="hover:bg-gray-50">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm font-medium text-gray-900">{item.invoiceNumber}</div>
                        <div className="text-sm text-gray-500">{formatInvoiceDate(item.invoiceDate)}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">{item.companyName}</div>
                      </td>
                      <td className="px-6 py-4">
                        <div className="text-sm text-gray-900 max-w-xs truncate">{item.description}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">{item.ruleName || '-'}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">{item.accountingTemplateCode || '-'}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">{item.department || '-'}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        {getResultBadge(item.result)}
                        {item.errorMessage && (
                          <div className="text-xs text-red-600 mt-1" title={item.errorMessage}>
                            {item.errorMessage.length > 50 ? `${item.errorMessage.substring(0, 50)}...` : item.errorMessage}
                          </div>
                        )}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">{formatDate(item.timestamp)}</div>
                        <div className="text-sm text-gray-500">{item.processedBy}</div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="flex space-x-2">
                          <button
                            onClick={() => item.invoiceId && handleClassifyInvoice(item.invoiceId)}
                            disabled={classifyingInvoiceId === item.invoiceId}
                            className="inline-flex items-center px-2 py-1 border border-transparent text-xs font-medium rounded text-indigo-700 bg-indigo-100 hover:bg-indigo-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed"
                            title="Klasifikovat tuto fakturu"
                          >
                            {classifyingInvoiceId === item.invoiceId ? (
                              <div className="w-3 h-3 mr-1 animate-spin rounded-full border border-indigo-700 border-t-transparent"></div>
                            ) : (
                              <Play className="w-3 h-3 mr-1" />
                            )}
                            Klasifikovat
                          </button>
                          <button
                            onClick={() => item.companyName ? handleCreateRule(item.companyName) : undefined}
                            disabled={!item.companyName}
                            className="inline-flex items-center px-2 py-1 border border-transparent text-xs font-medium rounded text-emerald-700 bg-emerald-100 hover:bg-emerald-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-emerald-500 disabled:opacity-50 disabled:cursor-not-allowed"
                            title="Vytvořit pravidlo pro tuto firmu"
                          >
                            <Plus className="w-3 h-3 mr-1" />
                            Vytvořit pravidlo
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Pagination */}
            {historyData && historyData.totalPages && historyData.totalPages > 1 && (
              <div className="bg-white px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6">
                <div className="flex-1 flex justify-between sm:hidden">
                  <button
                    onClick={() => setPage(Math.max(1, page - 1))}
                    disabled={page === 1}
                    className="relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Předchozí
                  </button>
                  <button
                    onClick={() => setPage(Math.min(historyData.totalPages || 1, page + 1))}
                    disabled={page === (historyData.totalPages || 1)}
                    className="ml-3 relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    Další
                  </button>
                </div>
                <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                  <div className="flex items-center space-x-3">
                    <p className="text-xs text-gray-600">
                      {((page - 1) * pageSize) + 1}-
                      {Math.min(page * pageSize, historyData.totalCount || 0)}{' '}
                      z {historyData.totalCount || 0}
                    </p>
                    <div className="flex items-center space-x-1">
                      <span className="text-xs text-gray-600">Zobrazit:</span>
                      <select
                        value={pageSize}
                        onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                        className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      >
                        <option value={10}>10</option>
                        <option value={20}>20</option>
                        <option value={50}>50</option>
                        <option value={100}>100</option>
                      </select>
                    </div>
                  </div>
                  <div>
                    <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
                      <button
                        onClick={() => setPage(Math.max(1, page - 1))}
                        disabled={page === 1}
                        className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        <span className="sr-only">Previous</span>
                        <ChevronLeft className="h-5 w-5" aria-hidden="true" />
                      </button>
                      
                      {/* Page numbers */}
                      {[...Array(Math.min(5, historyData.totalPages || 1))].map((_, i) => {
                        const totalPages = historyData.totalPages || 1;
                        const pageNum = Math.max(1, Math.min(
                          totalPages - 4,
                          Math.max(1, page - 2)
                        )) + i;
                        
                        if (pageNum > totalPages) return null;
                        
                        return (
                          <button
                            key={pageNum}
                            onClick={() => setPage(pageNum)}
                            className={`relative inline-flex items-center px-4 py-2 border text-sm font-medium ${
                              page === pageNum
                                ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                                : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                            }`}
                          >
                            {pageNum}
                          </button>
                        );
                      })}
                      
                      <button
                        onClick={() => setPage(Math.min(historyData.totalPages || 1, page + 1))}
                        disabled={page === (historyData.totalPages || 1)}
                        className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        <span className="sr-only">Next</span>
                        <ChevronRight className="h-5 w-5" aria-hidden="true" />
                      </button>
                    </nav>
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {/* Rule Creation Modal */}
      {showRuleModal && (
        <div className="fixed inset-0 bg-gray-600 bg-opacity-50 flex items-center justify-center p-4 z-50">
          <div className="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto">
            <div className="p-6">
              <RuleForm
                rule={null}
                onSubmit={handleRuleSubmit}
                onCancel={handleRuleCancel}
                isLoading={createRuleMutation.isPending}
                prefillCompanyName={prefillCompanyName}
              />
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ClassificationHistoryPage;