import React from "react";
import { useRunningJobsForGiftPackage } from "../../../api/hooks/useGiftPackageManufacturing";

interface RunningJobIndicatorProps {
  giftPackageCode: string;
  className?: string;
}

const RunningJobIndicator: React.FC<RunningJobIndicatorProps> = ({ 
  giftPackageCode, 
  className = "" 
}) => {
  const { data: runningJobsData } = useRunningJobsForGiftPackage(giftPackageCode);

  if (!runningJobsData?.hasRunningJobs) {
    return null;
  }

  const runningJobsCount = runningJobsData.runningJobs?.length || 0;

  return (
    <div className={`relative ${className}`}>
      <div
        className="w-2 h-2 bg-red-500 rounded-full animate-pulse"
        title={`${runningJobsCount} běžící výrob${runningJobsCount === 1 ? 'a' : 'y'}`}
      />
      {runningJobsCount > 1 && (
        <div className="absolute -top-1 -right-1 bg-red-600 text-white text-xs rounded-full w-3 h-3 flex items-center justify-center font-bold">
          {runningJobsCount}
        </div>
      )}
    </div>
  );
};

export default RunningJobIndicator;