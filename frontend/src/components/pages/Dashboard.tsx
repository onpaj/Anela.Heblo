import React, { useState } from "react";
import {
  useRecentAuditLogs,
  useRecentAuditSummary,
} from "../../api/hooks/useAudit";
import {
  useLiveHealthCheck,
  useReadyHealthCheck,
} from "../../api/hooks/useHealth";
import {
  useManualCatalogRefresh,
  refreshOperations,
} from "../../api/hooks/useManualCatalogRefresh";
import { useRecalculatePurchasePrice } from "../../api/hooks/useRecalculatePurchasePrice";
import { RecalculatePurchasePriceRequest } from "../../api/generated/api-client";
import {
  CheckCircle,
  XCircle,
  Clock,
  Activity,
  AlertTriangle,
  Database,
  RefreshCw,
  Settings,
  Calculator,
  Heart,
  Shield,
} from "lucide-react";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const Dashboard: React.FC = () => {
  const [activeTab, setActiveTab] = useState("overview");
  const [currentRefreshOperation, setCurrentRefreshOperation] = useState<
    string | null
  >(null);

  const {
    data: auditLogs,
    isLoading: isLogsLoading,
    error: logsError,
  } = useRecentAuditLogs(50);

  const {
    data: auditSummary,
    isLoading: isSummaryLoading,
    error: summaryError,
  } = useRecentAuditSummary();

  const {
    data: liveHealthData,
    isLoading: isLiveHealthLoading,
    error: liveHealthError,
  } = useLiveHealthCheck();

  const {
    data: readyHealthData,
    isLoading: isReadyHealthLoading,
    error: readyHealthError,
  } = useReadyHealthCheck();

  const manualRefreshMutation = useManualCatalogRefresh();
  const recalculatePricesMutation = useRecalculatePurchasePrice();

  const handleManualRefresh = async (
    methodName: string,
    operationKey: string,
  ) => {
    setCurrentRefreshOperation(operationKey);

    try {
      await manualRefreshMutation.mutateAsync(methodName);
      // Optionally refresh audit data after successful operation
      // You could add success notification here
    } catch (error) {
      console.error("Failed to refresh:", error);
      // You could add error notification here
    } finally {
      setCurrentRefreshOperation(null);
    }
  };

  const handleRecalculatePrice = async (
    operationKey: string,
    recalculateAll: boolean,
    productCode?: string,
  ) => {
    setCurrentRefreshOperation(operationKey);

    try {
      const request = new RecalculatePurchasePriceRequest({
        productCode,
        recalculateAll,
        forceReload: false,
      });

      await recalculatePricesMutation.mutateAsync(request);
      // Success notification could be added here
    } catch (error) {
      console.error("Failed to recalculate prices:", error);
      // Error notification could be added here
    } finally {
      setCurrentRefreshOperation(null);
    }
  };

  const formatDateTime = (dateString: string) => {
    return new Date(dateString).toLocaleString("cs-CZ", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  };

  const formatDuration = (durationString: string) => {
    const duration = parseFloat(durationString);
    if (duration < 1000) {
      return `${Math.round(duration)}ms`;
    }
    return `${(duration / 1000).toFixed(1)}s`;
  };

  const getStatusIcon = (success: boolean) => {
    return success ? (
      <CheckCircle className="w-5 h-5 text-emerald-500" />
    ) : (
      <XCircle className="w-5 h-5 text-red-500" />
    );
  };

  const getStatusBadge = (success: boolean) => {
    return success ? (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 text-emerald-800">
        Úspěšné
      </span>
    ) : (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
        Chyba
      </span>
    );
  };

  const getSummaryStats = () => {
    if (!auditSummary?.summary) return null;

    const totalRequests = auditSummary.summary.reduce(
      (sum, item) => sum + item.totalRequests,
      0,
    );
    const successfulRequests = auditSummary.summary.reduce(
      (sum, item) => sum + item.successfulRequests,
      0,
    );
    const failedRequests = auditSummary.summary.reduce(
      (sum, item) => sum + item.failedRequests,
      0,
    );
    const successRate =
      totalRequests > 0
        ? ((successfulRequests / totalRequests) * 100).toFixed(1)
        : "0";

    return { totalRequests, successfulRequests, failedRequests, successRate };
  };

  const getHealthStatusIcon = (
    status: string,
    isLoading: boolean,
    hasError: boolean,
  ) => {
    if (isLoading) {
      return <RefreshCw className="h-6 w-6 text-gray-400 animate-spin" />;
    }
    if (hasError) {
      return <XCircle className="h-6 w-6 text-red-500" />;
    }
    switch (status) {
      case "Healthy":
        return <CheckCircle className="h-6 w-6 text-emerald-500" />;
      case "Degraded":
        return <AlertTriangle className="h-6 w-6 text-yellow-500" />;
      case "Unhealthy":
        return <XCircle className="h-6 w-6 text-red-500" />;
      default:
        return <AlertTriangle className="h-6 w-6 text-gray-400" />;
    }
  };

  const getHealthStatusColor = (
    status: string,
    isLoading: boolean,
    hasError: boolean,
  ) => {
    if (isLoading) return "text-gray-900";
    if (hasError) return "text-red-900";
    switch (status) {
      case "Healthy":
        return "text-emerald-900";
      case "Degraded":
        return "text-yellow-900";
      case "Unhealthy":
        return "text-red-900";
      default:
        return "text-gray-900";
    }
  };

  const stats = getSummaryStats();

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold text-gray-900">
          Administrační dashboard
        </h1>
        <p className="mt-2 text-gray-600">
          Přehled systémové aktivity a audit logů
        </p>
      </div>

      {/* Content Area */}
      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        {/* Loading States */}
        {(isLogsLoading || isSummaryLoading) && (
          <div className="flex items-center justify-center py-8">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
            <span className="ml-2 text-gray-600">Načítám data...</span>
          </div>
        )}

        {/* Error States */}
        {(logsError || summaryError) && (
          <div className="mb-8 p-4 bg-red-50 border border-red-200 rounded-lg">
            <div className="flex items-center">
              <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
              <h3 className="text-red-800 font-medium">
                Chyba při načítání dat
              </h3>
            </div>
            <p className="mt-1 text-red-700 text-sm">
              {logsError?.message || summaryError?.message || "Neznámá chyba"}
            </p>
          </div>
        )}

        {/* Tabs */}
        <div className="border-b border-gray-200 mb-6">
          <nav className="-mb-px flex space-x-8">
            <button
              onClick={() => setActiveTab("overview")}
              className={`py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === "overview"
                  ? "border-indigo-500 text-indigo-600"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <Activity className="w-4 h-4 inline mr-2" />
              Přehled
            </button>
            <button
              onClick={() => setActiveTab("logs")}
              className={`py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === "logs"
                  ? "border-indigo-500 text-indigo-600"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <Database className="w-4 h-4 inline mr-2" />
              Audit logy
            </button>
            <button
              onClick={() => setActiveTab("manual-refresh")}
              className={`py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === "manual-refresh"
                  ? "border-indigo-500 text-indigo-600"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <Settings className="w-4 h-4 inline mr-2" />
              Manuální načítání
            </button>
            <button
              onClick={() => setActiveTab("manual-actions")}
              className={`py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === "manual-actions"
                  ? "border-indigo-500 text-indigo-600"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <Calculator className="w-4 h-4 inline mr-2" />
              Manuální akce
            </button>
          </nav>
        </div>

        {/* Stats Cards */}
        {stats && activeTab === "overview" && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
            {/* Total Requests */}
            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-5">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <Activity className="h-6 w-6 text-gray-400" />
                  </div>
                  <div className="ml-5 w-0 flex-1">
                    <dl>
                      <dt className="text-sm font-medium text-gray-500 truncate">
                        Celkem požadavků
                      </dt>
                      <dd className="text-lg font-medium text-gray-900">
                        {stats.totalRequests}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            {/* Successful Requests */}
            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-5">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <CheckCircle className="h-6 w-6 text-emerald-400" />
                  </div>
                  <div className="ml-5 w-0 flex-1">
                    <dl>
                      <dt className="text-sm font-medium text-gray-500 truncate">
                        Úspěšné
                      </dt>
                      <dd className="text-lg font-medium text-gray-900">
                        {stats.successfulRequests}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            {/* Failed Requests */}
            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-5">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <XCircle className="h-6 w-6 text-red-400" />
                  </div>
                  <div className="ml-5 w-0 flex-1">
                    <dl>
                      <dt className="text-sm font-medium text-gray-500 truncate">
                        Chybné
                      </dt>
                      <dd className="text-lg font-medium text-gray-900">
                        {stats.failedRequests}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            {/* Success Rate */}
            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-5">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <Clock className="h-6 w-6 text-blue-400" />
                  </div>
                  <div className="ml-5 w-0 flex-1">
                    <dl>
                      <dt className="text-sm font-medium text-gray-500 truncate">
                        Úspěšnost
                      </dt>
                      <dd className="text-lg font-medium text-gray-900">
                        {stats.successRate}%
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Summary Table */}
        {activeTab === "overview" && auditSummary?.summary && (
          <div className="bg-white shadow overflow-hidden sm:rounded-md mb-8 flex flex-col flex-1 min-h-0">
            <div className="px-4 py-5 sm:px-6 flex-shrink-0">
              <h3 className="text-lg leading-6 font-medium text-gray-900">
                Souhrn podle typu dat (posledních 7 dní)
              </h3>
              <p className="mt-1 max-w-2xl text-sm text-gray-500">
                Statistiky načítání dat podle zdroje a typu
              </p>
            </div>
            <div className="flex-1 overflow-auto min-h-0">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50 sticky top-0 z-10">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Typ dat / Zdroj
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Požadavky
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Úspěšnost
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Záznamy
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Průměrná doba
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Poslední úspěch
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {auditSummary.summary.map((item, index) => (
                    <tr key={index} className="hover:bg-gray-50">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm font-medium text-gray-900">
                          {item.dataType}
                        </div>
                        <div className="text-sm text-gray-500">
                          {item.source}
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">
                          {item.totalRequests}
                        </div>
                        <div className="text-sm text-gray-500">
                          {item.failedRequests > 0 && (
                            <span className="text-red-600">
                              {item.failedRequests} chyb
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm text-gray-900">
                          {item.totalRequests > 0
                            ? `${((item.successfulRequests / item.totalRequests) * 100).toFixed(1)}%`
                            : "0%"}
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {item.totalRecords.toLocaleString("cs-CZ")}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {item.averageDuration > 0
                          ? `${item.averageDuration.toFixed(0)}ms`
                          : "-"}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                        {item.lastSuccessfulLoad
                          ? formatDateTime(item.lastSuccessfulLoad)
                          : "Nikdy"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Recent Logs Table */}
        {activeTab === "logs" && auditLogs?.logs && (
          <div className="bg-white shadow overflow-hidden sm:rounded-md mb-8 flex flex-col flex-1 min-h-0">
            <div className="px-4 py-5 sm:px-6 flex-shrink-0">
              <h3 className="text-lg leading-6 font-medium text-gray-900">
                Poslední audit logy (24 hodin)
              </h3>
              <p className="mt-1 max-w-2xl text-sm text-gray-500">
                Zobrazeno posledních {auditLogs.logs.length} záznamů
              </p>
            </div>
            <div className="flex-1 overflow-auto min-h-0">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50 sticky top-0 z-10">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Čas
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Status
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Typ / Zdroj
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Záznamy
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Doba trvání
                    </th>
                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                      Chyba
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {auditLogs.logs.map((log) => (
                    <tr
                      key={log.id}
                      className={`hover:bg-gray-50 ${!log.success ? "bg-red-50" : ""}`}
                    >
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {formatDateTime(log.timestamp)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="flex items-center">
                          {getStatusIcon(log.success)}
                          <span className="ml-2">
                            {getStatusBadge(log.success)}
                          </span>
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div className="text-sm font-medium text-gray-900">
                          {log.dataType}
                        </div>
                        <div className="text-sm text-gray-500">
                          {log.source}
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {log.recordCount.toLocaleString("cs-CZ")}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                        {formatDuration(log.duration)}
                      </td>
                      <td className="px-6 py-4 text-sm text-red-600 max-w-xs truncate">
                        {log.errorMessage || "-"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {/* Manual Refresh Tab */}
        {activeTab === "manual-refresh" && (
          <div className="bg-white shadow overflow-hidden sm:rounded-md">
            <div className="px-4 py-5 sm:px-6">
              <h3 className="text-lg leading-6 font-medium text-gray-900">
                Manuální načítání dat
              </h3>
              <p className="mt-1 max-w-2xl text-sm text-gray-500">
                Spustit načítání jednotlivých typů dat z externích zdrojů
              </p>
            </div>
            <div className="px-4 py-5 sm:p-6">
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                {refreshOperations.map((operation) => (
                  <div
                    key={operation.key}
                    className="bg-gray-50 p-4 rounded-lg"
                  >
                    <div className="mb-3">
                      <h4 className="text-sm font-medium text-gray-900 mb-1">
                        {operation.name}
                      </h4>
                      <p className="text-xs text-gray-500">
                        {operation.description}
                      </p>
                    </div>
                    <button
                      onClick={() =>
                        handleManualRefresh(operation.methodName, operation.key)
                      }
                      disabled={
                        currentRefreshOperation === operation.key ||
                        manualRefreshMutation.isPending
                      }
                      className={`w-full inline-flex items-center justify-center px-3 py-2 border text-sm font-medium rounded-md focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 ${
                        currentRefreshOperation === operation.key
                          ? "border-indigo-300 text-indigo-700 bg-indigo-50 cursor-not-allowed"
                          : manualRefreshMutation.isPending
                            ? "border-gray-300 text-gray-400 bg-gray-100 cursor-not-allowed"
                            : "border-indigo-300 text-indigo-700 bg-white hover:bg-indigo-50"
                      }`}
                    >
                      {currentRefreshOperation === operation.key ? (
                        <>
                          <RefreshCw className="w-4 h-4 mr-2 animate-spin" />
                          Načítá...
                        </>
                      ) : (
                        <>
                          <RefreshCw className="w-4 h-4 mr-2" />
                          Načíst
                        </>
                      )}
                    </button>
                  </div>
                ))}
              </div>

              {/* Status Messages */}
              {manualRefreshMutation.isError && (
                <div className="mt-6 p-4 bg-red-50 border border-red-200 rounded-lg">
                  <div className="flex items-center">
                    <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
                    <h4 className="text-red-800 font-medium">
                      Chyba při načítání dat
                    </h4>
                  </div>
                  <p className="mt-1 text-red-700 text-sm">
                    {manualRefreshMutation.error?.message || "Neznámá chyba"}
                  </p>
                </div>
              )}

              {manualRefreshMutation.isSuccess && !currentRefreshOperation && (
                <div className="mt-6 p-4 bg-emerald-50 border border-emerald-200 rounded-lg">
                  <div className="flex items-center">
                    <CheckCircle className="w-5 h-5 text-emerald-500 mr-2" />
                    <h4 className="text-emerald-800 font-medium">
                      Data úspěšně načtena
                    </h4>
                  </div>
                  <p className="mt-1 text-emerald-700 text-sm">
                    Operace byla dokončena úspěšně.
                  </p>
                </div>
              )}
            </div>
          </div>
        )}

        {/* Manual Actions Tab */}
        {activeTab === "manual-actions" && (
          <div className="bg-white shadow overflow-hidden sm:rounded-md">
            <div className="px-4 py-5 sm:px-6">
              <h3 className="text-lg leading-6 font-medium text-gray-900">
                Manuální akce
              </h3>
              <p className="mt-1 max-w-2xl text-sm text-gray-500">
                Spustit speciální operace a přepočty
              </p>
            </div>
            <div className="px-4 py-5 sm:p-6">
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                {/* Recalculate All Products with BoM */}
                <div className="bg-gray-50 p-4 rounded-lg">
                  <div className="mb-3">
                    <h4 className="text-sm font-medium text-gray-900 mb-1">
                      Přepočet všech cen
                    </h4>
                    <p className="text-xs text-gray-500">
                      Přepočítá nákupní ceny všech produktů s kusovníkem
                    </p>
                  </div>
                  <button
                    onClick={() =>
                      handleRecalculatePrice("recalculate-all", true)
                    }
                    disabled={
                      currentRefreshOperation === "recalculate-all" ||
                      recalculatePricesMutation.isPending
                    }
                    className={`w-full inline-flex items-center justify-center px-3 py-2 border text-sm font-medium rounded-md focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 ${
                      currentRefreshOperation === "recalculate-all"
                        ? "border-indigo-300 text-indigo-700 bg-indigo-50 cursor-not-allowed"
                        : recalculatePricesMutation.isPending
                          ? "border-gray-300 text-gray-400 bg-gray-100 cursor-not-allowed"
                          : "border-indigo-300 text-indigo-700 bg-white hover:bg-indigo-50"
                    }`}
                  >
                    {currentRefreshOperation === "recalculate-all" ? (
                      <>
                        <Calculator className="w-4 h-4 mr-2 animate-pulse" />
                        Přepočítává...
                      </>
                    ) : (
                      <>
                        <Calculator className="w-4 h-4 mr-2" />
                        Přepočítat vše
                      </>
                    )}
                  </button>
                </div>

                {/* Future manual actions can be added here */}
              </div>

              {/* Status Messages for Price Recalculation */}
              {recalculatePricesMutation.isError && (
                <div className="mt-6 p-4 bg-red-50 border border-red-200 rounded-lg">
                  <div className="flex items-center">
                    <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
                    <h4 className="text-red-800 font-medium">
                      Chyba při přepočtu cen
                    </h4>
                  </div>
                  <p className="mt-1 text-red-700 text-sm">
                    {recalculatePricesMutation.error?.message ||
                      "Neznámá chyba"}
                  </p>
                </div>
              )}

              {recalculatePricesMutation.isSuccess &&
                !currentRefreshOperation &&
                recalculatePricesMutation.data && (
                  <div className="mt-6 p-4 bg-emerald-50 border border-emerald-200 rounded-lg">
                    <div className="flex items-center">
                      <CheckCircle className="w-5 h-5 text-emerald-500 mr-2" />
                      <h4 className="text-emerald-800 font-medium">
                        Přepočet cen dokončen
                      </h4>
                    </div>
                    <div className="mt-2 text-emerald-700 text-sm">
                      <p>
                        Celkem zpracováno:{" "}
                        {recalculatePricesMutation.data.totalCount || 0}{" "}
                        produktů
                      </p>
                      <p>
                        Úspěšně:{" "}
                        {recalculatePricesMutation.data.successCount || 0}
                      </p>
                      {(recalculatePricesMutation.data.failedCount || 0) >
                        0 && (
                        <p>
                          Chybné: {recalculatePricesMutation.data.failedCount}
                        </p>
                      )}
                    </div>

                    {/* Show failed products if any */}
                    {recalculatePricesMutation.data.processedProducts &&
                      recalculatePricesMutation.data.processedProducts.some(
                        (p) => !p.success,
                      ) && (
                        <div className="mt-3">
                          <h5 className="text-sm font-medium text-red-800 mb-2">
                            Produkty s chybami:
                          </h5>
                          <div className="space-y-1">
                            {recalculatePricesMutation.data.processedProducts
                              .filter((p) => !p.success)
                              .map((product, index) => (
                                <div
                                  key={index}
                                  className="text-xs text-red-700"
                                >
                                  <span className="font-mono">
                                    {product.productCode}
                                  </span>
                                  : Chyba {product.errorCode}
                                </div>
                              ))}
                          </div>
                        </div>
                      )}
                  </div>
                )}
            </div>
          </div>
        )}

        {/* Empty State */}
        {activeTab === "logs" &&
          auditLogs?.logs &&
          auditLogs.logs.length === 0 && (
            <div className="text-center py-12">
              <Database className="mx-auto h-12 w-12 text-gray-400" />
              <h3 className="mt-2 text-sm font-medium text-gray-900">
                Žádné audit logy
              </h3>
              <p className="mt-1 text-sm text-gray-500">
                V posledních 24 hodinách nebyly zaznamenány žádné aktivity.
              </p>
            </div>
          )}
      </div>
    </div>
  );
};

export default Dashboard;
