import React, { useState } from "react";
import {
  useBackgroundTasks,
  useForceRefreshTask,
} from "../api/hooks/useBackgroundRefresh";
import { RefreshTaskDto } from "../api/generated/api-client";
import {
  RefreshCw,
  Clock,
  CheckCircle,
  XCircle,
  AlertTriangle,
  PlayCircle,
  ChevronRight,
} from "lucide-react";
import TaskHistoryModal from "./TaskHistoryModal";

const BackgroundTasksCard: React.FC = () => {
  const { data: tasks, isLoading, error } = useBackgroundTasks();
  const forceRefreshMutation = useForceRefreshTask();
  const [selectedTaskId, setSelectedTaskId] = useState<string | null>(null);
  const [refreshingTaskId, setRefreshingTaskId] = useState<string | null>(null);

  const handleForceRefresh = async (taskId: string) => {
    setRefreshingTaskId(taskId);
    try {
      await forceRefreshMutation.mutateAsync(taskId);
    } catch (error) {
      console.error("Failed to force refresh task:", error);
    } finally {
      setRefreshingTaskId(null);
    }
  };

  const formatDuration = (timeSpan: string): string => {
    // TimeSpan format: "hh:mm:ss" or "dd.hh:mm:ss"
    const parts = timeSpan.split(":");
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);

    if (hours >= 24) {
      const days = Math.floor(hours / 24);
      return `${days}d ${hours % 24}h`;
    } else if (hours > 0) {
      return `${hours}h ${minutes}m`;
    } else {
      return `${minutes}m`;
    }
  };

  const formatDateTime = (date: Date | string | undefined | null): string => {
    if (!date) return "Nikdy";
    const dateObj = typeof date === 'string' ? new Date(date) : date;
    return dateObj.toLocaleString("cs-CZ", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  };

  const getTimeUntilNextRun = (nextScheduledRun: Date | string | undefined | null): string => {
    if (!nextScheduledRun) return "N/A";

    const now = new Date();
    const nextRun = typeof nextScheduledRun === 'string' ? new Date(nextScheduledRun) : nextScheduledRun;
    const diffMs = nextRun.getTime() - now.getTime();

    if (diffMs < 0) {
      return "Spouští se...";
    }

    const diffMinutes = Math.floor(diffMs / 60000);
    if (diffMinutes < 60) {
      return `za ${diffMinutes} min`;
    }

    const diffHours = Math.floor(diffMinutes / 60);
    if (diffHours < 24) {
      return `za ${diffHours}h ${diffMinutes % 60}m`;
    }

    const diffDays = Math.floor(diffHours / 24);
    return `za ${diffDays}d ${diffHours % 24}h`;
  };

  const getStatusBadge = (task: RefreshTaskDto) => {
    if (!task.enabled) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
          Vypnuto
        </span>
      );
    }

    if (!task.lastExecution) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 text-blue-800">
          Čeká
        </span>
      );
    }

    const status = task.lastExecution.status;

    if (status === "Running") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
          <RefreshCw className="w-3 h-3 mr-1 animate-spin" />
          Běží
        </span>
      );
    }

    if (status === "Completed") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 text-emerald-800">
          <CheckCircle className="w-3 h-3 mr-1" />
          Úspěch
        </span>
      );
    }

    if (status === "Failed") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
          <XCircle className="w-3 h-3 mr-1" />
          Chyba
        </span>
      );
    }

    if (status === "Cancelled") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
          Zrušeno
        </span>
      );
    }

    return null;
  };

  if (isLoading) {
    return (
      <div className="bg-white shadow overflow-hidden sm:rounded-md">
        <div className="px-4 py-5 sm:px-6">
          <h3 className="text-lg leading-6 font-medium text-gray-900">
            Background Refresh Tasky
          </h3>
        </div>
        <div className="px-4 py-8 flex items-center justify-center">
          <RefreshCw className="w-5 h-5 text-indigo-600 animate-spin mr-2" />
          <span className="text-gray-600">Načítám tasky...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-white shadow overflow-hidden sm:rounded-md">
        <div className="px-4 py-5 sm:px-6">
          <h3 className="text-lg leading-6 font-medium text-gray-900">
            Background Refresh Tasky
          </h3>
        </div>
        <div className="px-4 py-5 bg-red-50 border border-red-200 rounded-lg m-4">
          <div className="flex items-center">
            <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
            <h4 className="text-red-800 font-medium">Chyba při načítání tasků</h4>
          </div>
          <p className="mt-1 text-red-700 text-sm">{error.message}</p>
        </div>
      </div>
    );
  }

  // Sort tasks: Failed first, then by next scheduled run (earliest first)
  const sortedTasks = tasks ? [...tasks].sort((a, b) => {
    // Priority 1: Failed tasks first
    const aFailed = a.lastExecution?.status === "Failed";
    const bFailed = b.lastExecution?.status === "Failed";

    if (aFailed && !bFailed) return -1;
    if (!aFailed && bFailed) return 1;

    // Priority 2: Sort by next scheduled run (earliest first)
    if (!a.nextScheduledRun && !b.nextScheduledRun) return 0;
    if (!a.nextScheduledRun) return 1; // Tasks without next run go last
    if (!b.nextScheduledRun) return -1;

    const aTime = typeof a.nextScheduledRun === 'string' ? new Date(a.nextScheduledRun).getTime() : a.nextScheduledRun.getTime();
    const bTime = typeof b.nextScheduledRun === 'string' ? new Date(b.nextScheduledRun).getTime() : b.nextScheduledRun.getTime();

    return aTime - bTime;
  }) : [];

  return (
    <>
      <div className="bg-white shadow overflow-hidden sm:rounded-md">
        <div className="px-4 py-5 sm:px-6">
          <h3 className="text-lg leading-6 font-medium text-gray-900">
            Background Refresh Tasky
          </h3>
          <p className="mt-1 max-w-2xl text-sm text-gray-500">
            Přehled automatických úloh pro načítání dat na pozadí
          </p>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Task ID
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Nastavení
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Poslední spuštění
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Příští spuštění
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Akce
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {sortedTasks.map((task) => (
                <tr
                  key={task.taskId}
                  className={`hover:bg-gray-50 ${
                    task.lastExecution?.status === "Failed" ? "bg-red-50" : ""
                  }`}
                >
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div className="text-sm font-medium text-gray-900">
                      {task.taskId}
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    {getStatusBadge(task)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div className="text-sm text-gray-900">
                      <div className="flex items-center">
                        <Clock className="w-4 h-4 mr-1 text-gray-400" />
                        <span>Start: {formatDuration(task.initialDelay!)}</span>
                      </div>
                      <div className="flex items-center mt-1">
                        <RefreshCw className="w-4 h-4 mr-1 text-gray-400" />
                        <span>Interval: {formatDuration(task.refreshInterval!)}</span>
                      </div>
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    {task.lastExecution ? (
                      <div className="text-sm text-gray-900">
                        <div>{formatDateTime(task.lastExecution.startedAt!)}</div>
                        {task.lastExecution.duration && (
                          <div className="text-xs text-gray-500">
                            Trvání: {formatDuration(task.lastExecution.duration)}
                          </div>
                        )}
                        {task.lastExecution.errorMessage && (
                          <div className="text-xs text-red-600 truncate max-w-xs">
                            {task.lastExecution.errorMessage}
                          </div>
                        )}
                      </div>
                    ) : (
                      <span className="text-sm text-gray-500">Nikdy</span>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    {task.enabled ? (
                      <div className="text-sm text-gray-900">
                        <div>{formatDateTime(task.nextScheduledRun)}</div>
                        <div className="text-xs text-gray-500">
                          {getTimeUntilNextRun(task.nextScheduledRun)}
                        </div>
                      </div>
                    ) : (
                      <span className="text-sm text-gray-500">-</span>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                    <div className="flex items-center space-x-2">
                      <button
                        onClick={() => handleForceRefresh(task.taskId!)}
                        disabled={
                          refreshingTaskId === task.taskId ||
                          forceRefreshMutation.isPending ||
                          !task.enabled
                        }
                        className={`inline-flex items-center px-3 py-1.5 border rounded-md text-xs font-medium focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 ${
                          refreshingTaskId === task.taskId
                            ? "border-indigo-300 text-indigo-700 bg-indigo-50 cursor-not-allowed"
                            : !task.enabled
                              ? "border-gray-300 text-gray-400 bg-gray-100 cursor-not-allowed"
                              : "border-indigo-300 text-indigo-700 bg-white hover:bg-indigo-50"
                        }`}
                        title={!task.enabled ? "Task je vypnutý" : "Spustit nyní"}
                      >
                        {refreshingTaskId === task.taskId ? (
                          <>
                            <RefreshCw className="w-3 h-3 mr-1 animate-spin" />
                            Spouští...
                          </>
                        ) : (
                          <>
                            <PlayCircle className="w-3 h-3 mr-1" />
                            Spustit
                          </>
                        )}
                      </button>
                      <button
                        onClick={() => setSelectedTaskId(task.taskId!)}
                        className="inline-flex items-center px-3 py-1.5 border border-gray-300 rounded-md text-xs font-medium text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                        title="Zobrazit historii"
                      >
                        <ChevronRight className="w-3 h-3 mr-1" />
                        Historie
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {sortedTasks && sortedTasks.length === 0 && (
          <div className="text-center py-12 px-4">
            <Clock className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">
              Žádné background tasky
            </h3>
            <p className="mt-1 text-sm text-gray-500">
              Nejsou zaregistrovány žádné automatické úlohy.
            </p>
          </div>
        )}
      </div>

      {/* Task History Modal */}
      {selectedTaskId && (
        <TaskHistoryModal
          taskId={selectedTaskId}
          onClose={() => setSelectedTaskId(null)}
        />
      )}
    </>
  );
};

export default BackgroundTasksCard;
