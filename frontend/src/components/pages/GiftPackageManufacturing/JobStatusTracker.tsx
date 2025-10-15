import React, { useEffect, useState } from "react";
import { useGiftPackageManufactureJobStatus } from "../../../api/hooks/useGiftPackageManufacturing";
import { Loader2, CheckCircle, XCircle, Clock } from "lucide-react";

interface JobStatusTrackerProps {
  jobId: string;
  onJobCompleted: () => void;
}

const JobStatusTracker: React.FC<JobStatusTrackerProps> = ({ jobId, onJobCompleted }) => {
  const [hasStartedCompletionTimer, setHasStartedCompletionTimer] = useState(false);

  const { data: jobStatusResponse, isLoading, error } = useGiftPackageManufactureJobStatus(jobId);

  const jobStatus = jobStatusResponse?.jobStatus;
  const status = jobStatus?.status || "Unknown";

  // Compute isRunning based on status (same logic as backend)
  const isRunning = status === "Processing" || status === "Enqueued";

  // Remove job from tracking when completed
  useEffect(() => {
    console.log(`[JobStatusTracker] Job ${jobId}: status=${status}, isRunning=${isRunning}, hasStartedTimer=${hasStartedCompletionTimer}, jobStatus=`, jobStatus);

    // Only start the completion timer once when job becomes completed
    if (jobStatus && !isRunning && (status === "Succeeded" || status === "Failed") && !hasStartedCompletionTimer) {
      console.log(`[JobStatusTracker] Starting completion timer for job ${jobId}`);
      setHasStartedCompletionTimer(true);

      // Start timer - don't return cleanup, let it complete
      setTimeout(() => {
        console.log(`[JobStatusTracker] Timer fired! Calling onJobCompleted for job ${jobId}`);
        onJobCompleted();
      }, 5000); // Keep completed jobs visible for 5 seconds
    }
  }, [jobStatus, isRunning, status, hasStartedCompletionTimer, onJobCompleted, jobId]);

  const getStatusIcon = () => {
    if (isLoading) return <Loader2 className="w-4 h-4 animate-spin text-blue-500" />;
    
    switch (status) {
      case "Succeeded":
        return <CheckCircle className="w-4 h-4 text-green-500" />;
      case "Failed":
        return <XCircle className="w-4 h-4 text-red-500" />;
      case "Processing":
        return <Loader2 className="w-4 h-4 animate-spin text-blue-500" />;
      case "Enqueued":
        return <Clock className="w-4 h-4 text-yellow-500" />;
      default:
        return <Clock className="w-4 h-4 text-gray-500" />;
    }
  };

  const getStatusText = () => {
    switch (status) {
      case "Succeeded":
        return "Dokončeno";
      case "Failed":
        return "Chyba";
      case "Processing":
        return "Zpracovává se";
      case "Enqueued":
        return "Ve frontě";
      default:
        return "Neznámý";
    }
  };

  const getStatusColor = () => {
    switch (status) {
      case "Succeeded":
        return "text-green-700";
      case "Failed":
        return "text-red-700";
      case "Processing":
        return "text-blue-700";
      case "Enqueued":
        return "text-yellow-700";
      default:
        return "text-gray-700";
    }
  };

  if (error) {
    return (
      <div className="flex items-center space-x-2 text-sm">
        <XCircle className="w-4 h-4 text-red-500" />
        <span className="text-red-700">Chyba načítání statusu</span>
        <span className="text-gray-500 text-xs">({jobId.substring(0, Math.min(jobId.length, 8))}...)</span>
      </div>
    );
  }

  return (
    <div className="flex items-center space-x-2 text-sm">
      {getStatusIcon()}
      <span className={getStatusColor()}>{getStatusText()}</span>
      {jobStatus?.displayName ? (
        <span className="text-gray-900 text-sm font-semibold">{jobStatus.displayName}</span>
      ) : (
        <span className="text-gray-500 text-xs" title={jobId}>
          Job: {jobId.substring(0, Math.min(jobId.length, 8))}...
        </span>
      )}
      {jobStatus?.errorMessage && (
        <span className="text-red-600 text-xs">({jobStatus.errorMessage})</span>
      )}
    </div>
  );
};

export default JobStatusTracker;