import React from "react";
import BackgroundTasksCard from "../../BackgroundTasksCard";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";

const BackgroundTasks: React.FC = () => {
  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold text-gray-900">Background Refresh Tasky</h1>
        <p className="mt-2 text-gray-600">
          Přehled a správa automatických úloh pro načítání dat na pozadí
        </p>
      </div>

      {/* Content Area */}
      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        <BackgroundTasksCard />
      </div>
    </div>
  );
};

export default BackgroundTasks;
