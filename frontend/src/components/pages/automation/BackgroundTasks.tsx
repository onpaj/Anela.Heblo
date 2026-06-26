import React from "react";
import BackgroundTasksCard from "../../BackgroundTasksCard";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";
import { RefreshCw } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { QUERY_KEYS } from "../../../api/client";
import { useScreenView } from "../../../telemetry/useScreenView";

const BackgroundTasks: React.FC = () => {
  useScreenView('Automation', 'BackgroundTasks');
  const queryClient = useQueryClient();
  const [isRefreshing, setIsRefreshing] = React.useState(false);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    try {
      await queryClient.invalidateQueries({
        queryKey: QUERY_KEYS.backgroundRefresh,
      });
      // Wait a bit for the refetch to complete
      await new Promise((resolve) => setTimeout(resolve, 500));
    } finally {
      setIsRefreshing(false);
    }
  };

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <div className="flex items-start justify-between">
          <div>
            <h1 className="text-3xl font-bold text-gray-900 dark:text-graphite-text">
              Background Refresh Tasky
            </h1>
            <p className="mt-2 text-gray-600 dark:text-graphite-muted">
              Přehled a správa automatických úloh pro načítání dat na pozadí
            </p>
          </div>
          <button
            onClick={handleRefresh}
            disabled={isRefreshing}
            className="inline-flex items-center px-4 py-2 border border-indigo-300 dark:border-graphite-accent rounded-md text-sm font-medium text-indigo-700 dark:text-graphite-accent bg-white dark:bg-graphite-surface hover:bg-indigo-50 dark:hover:bg-graphite-accent/10 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            title="Obnovit data"
          >
            <RefreshCw
              className={`w-4 h-4 mr-2 ${isRefreshing ? "animate-spin" : ""}`}
            />
            Obnovit
          </button>
        </div>
      </div>

      {/* Content Area */}
      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        <BackgroundTasksCard />
      </div>
    </div>
  );
};

export default BackgroundTasks;
