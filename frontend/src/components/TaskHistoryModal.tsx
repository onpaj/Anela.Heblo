import React from "react";
import { useTaskHistory } from "../api/hooks/useBackgroundRefresh";
import { X, RefreshCw, CheckCircle, XCircle, Clock, AlertTriangle } from "lucide-react";
import { RefreshTaskExecutionLogDto } from "../api/generated/api-client";

interface TaskHistoryModalProps {
  taskId: string;
  onClose: () => void;
}

const TaskHistoryModal: React.FC<TaskHistoryModalProps> = ({ taskId, onClose }) => {
  const { data: history, isLoading, error } = useTaskHistory(taskId, 50);

  // Debug: Log metadata to console
  React.useEffect(() => {
    if (history && history.length > 0) {
      console.log("Task history metadata debug:", history.map(log => ({
        taskId: log.taskId,
        startedAt: log.startedAt,
        metadata: log.metadata,
        isForceRefreshValue: log.metadata?.["IsForceRefresh"],
        isForceRefreshType: typeof log.metadata?.["IsForceRefresh"]
      })));
    }
  }, [history]);

  const formatDateTime = (date: Date | string | undefined | null): string => {
    if (!date) return "N/A";
    const dateObj = typeof date === 'string' ? new Date(date) : date;
    return dateObj.toLocaleString("cs-CZ", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
  };

  const formatDuration = (timeSpan: string | undefined | null): string => {
    if (!timeSpan) return "N/A";

    // TimeSpan format: "hh:mm:ss.fffffff"
    const parts = timeSpan.split(":");
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    const seconds = parseFloat(parts[2]);

    if (hours > 0) {
      return `${hours}h ${minutes}m ${Math.floor(seconds)}s`;
    } else if (minutes > 0) {
      return `${minutes}m ${Math.floor(seconds)}s`;
    } else if (seconds >= 1) {
      return `${seconds.toFixed(1)}s`;
    } else {
      return `${(seconds * 1000).toFixed(0)}ms`;
    }
  };

  const getStatusBadge = (status: string | undefined) => {
    if (status === "Running") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800 dark:bg-amber-400/15 dark:text-amber-400">
          <RefreshCw className="w-3 h-3 mr-1 animate-spin" />
          Běží
        </span>
      );
    }

    if (status === "Completed") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 text-emerald-800 dark:bg-emerald-400/15 dark:text-emerald-400">
          <CheckCircle className="w-3 h-3 mr-1" />
          Úspěch
        </span>
      );
    }

    if (status === "Failed") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800 dark:bg-red-400/15 dark:text-red-400">
          <XCircle className="w-3 h-3 mr-1" />
          Chyba
        </span>
      );
    }

    if (status === "Cancelled") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-white/10 text-gray-800 dark:text-graphite-muted">
          Zrušeno
        </span>
      );
    }

    return <span className="text-xs text-gray-500 dark:text-graphite-muted">{status}</span>;
  };

  const getStatusIcon = (log: RefreshTaskExecutionLogDto) => {
    const status = log.status;

    if (status === "Running") {
      return <RefreshCw className="w-5 h-5 text-yellow-500 dark:text-amber-400 animate-spin" />;
    }

    if (status === "Completed") {
      return <CheckCircle className="w-5 h-5 text-emerald-500 dark:text-emerald-400" />;
    }

    if (status === "Failed") {
      return <XCircle className="w-5 h-5 text-red-500 dark:text-red-400" />;
    }

    if (status === "Cancelled") {
      return <Clock className="w-5 h-5 text-gray-500 dark:text-graphite-muted" />;
    }

    return <Clock className="w-5 h-5 text-gray-400 dark:text-graphite-faint" />;
  };

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      <div className="flex items-center justify-center min-h-screen px-4 pt-4 pb-20 text-center sm:block sm:p-0">
        {/* Background overlay */}
        <div
          className="fixed inset-0 transition-opacity bg-gray-500 bg-opacity-75"
          onClick={onClose}
        />

        {/* Modal panel */}
        <div className="inline-block w-full max-w-5xl my-8 overflow-hidden text-left align-middle transition-all transform bg-white dark:bg-graphite-surface shadow-xl rounded-lg">
          {/* Header */}
          <div className="bg-gray-50 dark:bg-graphite-surface-2 px-6 py-4 border-b border-gray-200 dark:border-graphite-border">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text">Historie tasku</h3>
                <p className="mt-1 text-sm text-gray-500 dark:text-graphite-muted font-mono">{taskId}</p>
              </div>
              <button
                onClick={onClose}
                className="text-gray-400 dark:text-graphite-faint hover:text-gray-500 dark:hover:text-graphite-muted focus:outline-none focus:ring-2 focus:ring-indigo-500 rounded-md p-1"
              >
                <X className="w-6 h-6" />
              </button>
            </div>
          </div>

          {/* Content */}
          <div className="px-6 py-4 max-h-[70vh] overflow-y-auto">
            {isLoading && (
              <div className="flex items-center justify-center py-8">
                <RefreshCw className="w-5 h-5 text-indigo-600 dark:text-graphite-accent animate-spin mr-2" />
                <span className="text-gray-600 dark:text-graphite-muted">Načítám historii...</span>
              </div>
            )}

            {error && (
              <div className="p-4 bg-red-50 dark:bg-red-400/15 border border-red-200 dark:border-red-400/30 rounded-lg">
                <div className="flex items-center">
                  <AlertTriangle className="w-5 h-5 text-red-500 dark:text-red-400 mr-2" />
                  <h4 className="text-red-800 dark:text-red-400 font-medium">Chyba při načítání historie</h4>
                </div>
                <p className="mt-1 text-red-700 dark:text-red-400 text-sm">{error.message}</p>
              </div>
            )}

            {history && history.length === 0 && (
              <div className="text-center py-12">
                <Clock className="mx-auto h-12 w-12 text-gray-400 dark:text-graphite-faint" />
                <h3 className="mt-2 text-sm font-medium text-gray-900 dark:text-graphite-text">Žádná historie</h3>
                <p className="mt-1 text-sm text-gray-500 dark:text-graphite-muted">
                  Tento task ještě nebyl nikdy spuštěn.
                </p>
              </div>
            )}

            {history && history.length > 0 && (
              <div className="space-y-4">
                {history.map((log, index) => (
                  <div
                    key={index}
                    className={`border rounded-lg p-4 ${
                      log.status === "Failed"
                        ? "border-red-200 dark:border-red-400/30 bg-red-50 dark:bg-red-400/15"
                        : log.status === "Running"
                          ? "border-yellow-200 dark:border-amber-400/30 bg-yellow-50 dark:bg-amber-400/15"
                          : "border-gray-200 dark:border-graphite-border bg-white dark:bg-graphite-surface"
                    }`}
                  >
                    <div className="flex items-start justify-between">
                      <div className="flex items-start space-x-3">
                        <div className="flex-shrink-0 mt-1">{getStatusIcon(log)}</div>
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center space-x-2">
                            {getStatusBadge(log.status)}
                            <span className="text-sm text-gray-500 dark:text-graphite-muted">
                              {formatDateTime(log.startedAt)}
                            </span>
                          </div>

                          <div className="mt-2 grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-2 text-sm">
                            <div>
                              <span className="text-gray-500 dark:text-graphite-muted">Začátek:</span>{" "}
                              <span className="text-gray-900 dark:text-graphite-text">
                                {formatDateTime(log.startedAt)}
                              </span>
                            </div>
                            <div>
                              <span className="text-gray-500 dark:text-graphite-muted">Konec:</span>{" "}
                              <span className="text-gray-900 dark:text-graphite-text">
                                {formatDateTime(log.completedAt)}
                              </span>
                            </div>
                            {log.duration && (
                              <div>
                                <span className="text-gray-500 dark:text-graphite-muted">Trvání:</span>{" "}
                                <span className="text-gray-900 dark:text-graphite-text">
                                  {formatDuration(log.duration)}
                                </span>
                              </div>
                            )}
                            {log.metadata &&
                             (log.metadata["IsForceRefresh"] === true ||
                              log.metadata["IsForceRefresh"] === "True" ||
                              log.metadata["IsForceRefresh"] === "true") && (
                              <div>
                                <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-indigo-100 dark:bg-graphite-accent/10 text-indigo-800 dark:text-graphite-accent">
                                  Manuální spuštění
                                </span>
                              </div>
                            )}
                          </div>

                          {log.errorMessage && (
                            <div className="mt-3 p-3 bg-red-100 dark:bg-red-400/15 border border-red-200 dark:border-red-400/30 rounded-md">
                              <div className="flex items-start">
                                <AlertTriangle className="w-4 h-4 text-red-500 dark:text-red-400 mt-0.5 mr-2 flex-shrink-0" />
                                <div className="flex-1">
                                  <p className="text-sm font-medium text-red-800 dark:text-red-400">
                                    Chybová zpráva:
                                  </p>
                                  <p className="mt-1 text-sm text-red-700 dark:text-red-400 whitespace-pre-wrap break-words">
                                    {log.errorMessage}
                                  </p>
                                </div>
                              </div>
                            </div>
                          )}

                          {log.metadata && Object.keys(log.metadata).length > 0 && (
                            <div className="mt-3">
                              <details className="text-xs">
                                <summary className="cursor-pointer text-gray-500 dark:text-graphite-muted hover:text-gray-700 dark:hover:text-graphite-muted font-medium">
                                  Metadata ({Object.keys(log.metadata).length})
                                </summary>
                                <div className="mt-2 p-2 bg-gray-50 dark:bg-graphite-surface-2 rounded border border-gray-200 dark:border-graphite-border">
                                  <pre className="text-xs text-gray-700 dark:text-graphite-muted overflow-x-auto">
                                    {JSON.stringify(log.metadata, null, 2)}
                                  </pre>
                                </div>
                              </details>
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="bg-gray-50 dark:bg-graphite-surface-2 px-6 py-4 border-t border-gray-200 dark:border-graphite-border flex justify-end">
            <button
              onClick={onClose}
              className="px-4 py-2 border border-gray-300 dark:border-graphite-border rounded-md text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
            >
              Zavřít
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default TaskHistoryModal;
