import React, { useState } from "react";
import {
  useBackgroundTasks,
  useForceRefreshTask,
  useRunHydrationTier,
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
  const runHydrationTierMutation = useRunHydrationTier();
  const [selectedTaskId, setSelectedTaskId] = useState<string | null>(null);
  const [refreshingTaskId, setRefreshingTaskId] = useState<string | null>(null);
  const [runningTier, setRunningTier] = useState<number | null>(null);

  // Group tasks by HydrationTier - must be called before any early returns
  const groupedTasks = React.useMemo(() => {
    if (!tasks) return new Map<number, RefreshTaskDto[]>();

    const groups = new Map<number, RefreshTaskDto[]>();

    tasks.forEach(task => {
      const tier = task.hydrationTier ?? 1;
      if (!groups.has(tier)) {
        groups.set(tier, []);
      }
      groups.get(tier)!.push(task);
    });

    // Sort tasks within each tier: Failed first, then by next scheduled run
    groups.forEach((tasksInTier) => {
      tasksInTier.sort((a, b) => {
        // Priority 1: Failed tasks first
        const aFailed = a.lastExecution?.status === "Failed";
        const bFailed = b.lastExecution?.status === "Failed";

        if (aFailed && !bFailed) return -1;
        if (!aFailed && bFailed) return 1;

        // Priority 2: Sort by next scheduled run (earliest first)
        if (!a.nextScheduledRun && !b.nextScheduledRun) return 0;
        if (!a.nextScheduledRun) return 1;
        if (!b.nextScheduledRun) return -1;

        const aTime = typeof a.nextScheduledRun === 'string' ? new Date(a.nextScheduledRun).getTime() : a.nextScheduledRun.getTime();
        const bTime = typeof b.nextScheduledRun === 'string' ? new Date(b.nextScheduledRun).getTime() : b.nextScheduledRun.getTime();

        return aTime - bTime;
      });
    });

    // Sort tiers in ascending order (Tier 1, Tier 2, etc.)
    const sortedEntries = Array.from(groups.entries()).sort((a, b) => a[0] - b[0]);
    return new Map(sortedEntries);
  }, [tasks]);

  const getTierBadgeColor = (tier: number): string => {
    const colors = [
      'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300',         // Tier 1
      'bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300', // Tier 2
      'bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300',   // Tier 3
      'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300', // Tier 4
      'bg-pink-100 text-pink-800 dark:bg-pink-900/30 dark:text-pink-300',         // Tier 5+
    ];
    return colors[Math.min(tier - 1, colors.length - 1)];
  };

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

  const handleRunTier = async (tier: number) => {
    setRunningTier(tier);
    try {
      await runHydrationTierMutation.mutateAsync(tier);
    } catch (error) {
      console.error(`Failed to run hydration tier ${tier}:`, error);
    } finally {
      setRunningTier(null);
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
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-800 dark:text-graphite-muted">
          Vypnuto
        </span>
      );
    }

    if (!task.lastExecution) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300">
          Čeká
        </span>
      );
    }

    const status = task.lastExecution.status;

    if (status === "Running") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 dark:bg-amber-900/30 text-yellow-800 dark:text-amber-300">
          <RefreshCw className="w-3 h-3 mr-1 animate-spin" />
          Běží
        </span>
      );
    }

    if (status === "Completed") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 dark:bg-emerald-900/30 text-emerald-800 dark:text-emerald-300">
          <CheckCircle className="w-3 h-3 mr-1" />
          Úspěch
        </span>
      );
    }

    if (status === "Failed") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300">
          <XCircle className="w-3 h-3 mr-1" />
          Chyba
        </span>
      );
    }

    if (status === "Cancelled") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-800 dark:text-graphite-muted">
          Zrušeno
        </span>
      );
    }

    return null;
  };

  if (isLoading) {
    return (
      <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark overflow-hidden sm:rounded-md">
        <div className="px-4 py-5 sm:px-6">
          <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">
            Background Refresh Tasky
          </h3>
        </div>
        <div className="px-4 py-8 flex items-center justify-center">
          <RefreshCw className="w-5 h-5 text-indigo-600 animate-spin mr-2" />
          <span className="text-gray-600 dark:text-graphite-muted">Načítám tasky...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark overflow-hidden sm:rounded-md">
        <div className="px-4 py-5 sm:px-6">
          <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">
            Background Refresh Tasky
          </h3>
        </div>
        <div className="px-4 py-5 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-900/40 rounded-lg m-4">
          <div className="flex items-center">
            <AlertTriangle className="w-5 h-5 text-red-500 dark:text-red-400 mr-2" />
            <h4 className="text-red-800 dark:text-red-300 font-medium">Chyba při načítání tasků</h4>
          </div>
          <p className="mt-1 text-red-700 dark:text-red-300 text-sm">{error.message}</p>
        </div>
      </div>
    );
  }

  return (
    <>
      <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark overflow-hidden sm:rounded-md">
        <div className="px-4 py-5 sm:px-6">
          <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">
            Background Refresh Tasky
          </h3>
          <p className="mt-1 max-w-2xl text-sm text-gray-500 dark:text-graphite-muted">
            Přehled automatických úloh pro načítání dat na pozadí
          </p>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider w-1/4">
                  Task ID
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider w-24">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider w-32">
                  Nastavení
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider w-40">
                  Poslední spuštění
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider w-40">
                  Příští spuštění
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider w-44">
                  Akce
                </th>
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {Array.from(groupedTasks.entries()).map(([tier, tasksInTier]) => (
                <React.Fragment key={tier}>
                  {/* Tier Header Row */}
                  <tr className="bg-gray-50 dark:bg-graphite-surface-2 border-t-2 border-gray-300 dark:border-graphite-border">
                    <td colSpan={6} className="px-4 py-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center">
                          <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getTierBadgeColor(tier)}`}>
                            Tier {tier}
                          </span>
                          <span className="ml-3 text-sm text-gray-600 dark:text-graphite-muted">
                            {tasksInTier.length} {tasksInTier.length === 1 ? 'task' : 'tasků'}
                          </span>
                        </div>
                        <button
                          onClick={() => handleRunTier(tier)}
                          disabled={runningTier !== null || refreshingTaskId !== null}
                          className={`inline-flex items-center px-3 py-1.5 border rounded-md text-xs font-medium focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 ${
                            runningTier === tier
                              ? "border-indigo-300 dark:border-graphite-accent text-indigo-700 dark:text-graphite-accent bg-indigo-50 dark:bg-graphite-accent/10 cursor-not-allowed"
                              : runningTier !== null || refreshingTaskId !== null
                                ? "border-gray-300 dark:border-graphite-border text-gray-400 dark:text-graphite-faint bg-gray-100 dark:bg-graphite-surface-2 cursor-not-allowed"
                                : "border-indigo-300 dark:border-graphite-accent text-indigo-700 dark:text-graphite-accent bg-white dark:bg-graphite-surface hover:bg-indigo-50 dark:hover:bg-graphite-accent/10"
                          }`}
                          title={`Spustit všechny tasky v tier ${tier} v pořadí`}
                        >
                          {runningTier === tier ? (
                            <>
                              <RefreshCw className="w-3 h-3 mr-1 animate-spin" />
                              Spouští tier...
                            </>
                          ) : (
                            <>
                              <PlayCircle className="w-3 h-3 mr-1" />
                              Spustit tier
                            </>
                          )}
                        </button>
                      </div>
                    </td>
                  </tr>

                  {/* Task Rows */}
                  {tasksInTier.map((task) => (
                    <tr
                      key={task.taskId}
                      className={`hover:bg-gray-50 dark:hover:bg-white/5 ${
                        task.lastExecution?.status === "Failed" ? "bg-red-50 dark:bg-red-900/30" : ""
                      }`}
                    >
                      <td className="px-6 py-4 whitespace-nowrap w-1/4">
                        <div className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                          {task.taskId}
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap w-24">
                        {getStatusBadge(task)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap w-32">
                        <div className="text-sm text-gray-900 dark:text-graphite-text">
                          <div className="flex items-center">
                            <Clock className="w-4 h-4 mr-1 text-gray-400 dark:text-graphite-faint" />
                            <span>Start: {formatDuration(task.initialDelay!)}</span>
                          </div>
                          <div className="flex items-center mt-1">
                            <RefreshCw className="w-4 h-4 mr-1 text-gray-400 dark:text-graphite-faint" />
                            <span>Interval: {formatDuration(task.refreshInterval!)}</span>
                          </div>
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap w-40">
                        {task.lastExecution ? (
                          <div className="text-sm text-gray-900 dark:text-graphite-text">
                            <div>{formatDateTime(task.lastExecution.startedAt!)}</div>
                            {task.lastExecution.duration && (
                              <div className="text-xs text-gray-500 dark:text-graphite-muted">
                                Trvání: {formatDuration(task.lastExecution.duration)}
                              </div>
                            )}
                            {task.lastExecution.errorMessage && (
                              <div className="text-xs text-red-600 dark:text-red-400 truncate max-w-xs">
                                {task.lastExecution.errorMessage}
                              </div>
                            )}
                          </div>
                        ) : (
                          <span className="text-sm text-gray-500 dark:text-graphite-muted">Nikdy</span>
                        )}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap w-40">
                        {task.enabled ? (
                          <div className="text-sm text-gray-900 dark:text-graphite-text">
                            <div>{formatDateTime(task.nextScheduledRun)}</div>
                            <div className="text-xs text-gray-500 dark:text-graphite-muted">
                              {getTimeUntilNextRun(task.nextScheduledRun)}
                            </div>
                          </div>
                        ) : (
                          <span className="text-sm text-gray-500 dark:text-graphite-muted">-</span>
                        )}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm font-medium w-44">
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
                                ? "border-indigo-300 dark:border-graphite-accent text-indigo-700 dark:text-graphite-accent bg-indigo-50 dark:bg-graphite-accent/10 cursor-not-allowed"
                                : !task.enabled
                                  ? "border-gray-300 dark:border-graphite-border text-gray-400 dark:text-graphite-faint bg-gray-100 dark:bg-graphite-surface-2 cursor-not-allowed"
                                  : "border-indigo-300 dark:border-graphite-accent text-indigo-700 dark:text-graphite-accent bg-white dark:bg-graphite-surface hover:bg-indigo-50 dark:hover:bg-graphite-accent/10"
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
                            className="inline-flex items-center px-3 py-1.5 border border-gray-300 dark:border-graphite-border rounded-md text-xs font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                            title="Zobrazit historii"
                          >
                            <ChevronRight className="w-3 h-3 mr-1" />
                            Historie
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </React.Fragment>
              ))}
            </tbody>
          </table>
        </div>

        {tasks && tasks.length === 0 && (
          <div className="text-center py-12 px-4">
            <Clock className="mx-auto h-12 w-12 text-gray-400 dark:text-graphite-faint" />
            <h3 className="mt-2 text-sm font-medium text-gray-900 dark:text-graphite-text">
              Žádné background tasky
            </h3>
            <p className="mt-1 text-sm text-gray-500 dark:text-graphite-muted">
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
