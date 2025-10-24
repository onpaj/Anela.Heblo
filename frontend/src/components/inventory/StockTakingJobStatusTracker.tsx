import React, { useEffect, useState } from "react";
import { useStockTakingJobStatus } from "../../api/hooks/useStockTaking";
import { Loader2, CheckCircle, XCircle, Clock } from "lucide-react";

interface StockTakingJobStatusTrackerProps {
  jobId: string;
  productCode: string;
  onJobCompleted: () => void;
}

const StockTakingJobStatusTracker: React.FC<StockTakingJobStatusTrackerProps> = ({ 
  jobId, 
  productCode, 
  onJobCompleted 
}) => {
  const [hasStartedCompletionTimer, setHasStartedCompletionTimer] = useState(false);

  const { data: jobStatus, isLoading, error } = useStockTakingJobStatus(jobId);

  const status = jobStatus?.status || "Unknown";
  const isCompleted = jobStatus?.isCompleted || false;

  // Remove job from tracking when completed
  useEffect(() => {
    console.log(`[StockTakingJobStatusTracker] Job ${jobId}: status=${status}, isCompleted=${isCompleted}, hasStartedTimer=${hasStartedCompletionTimer}, jobStatus=`, jobStatus);

    // Only start the completion timer once when job becomes completed
    if (jobStatus && isCompleted && !hasStartedCompletionTimer) {
      console.log(`[StockTakingJobStatusTracker] Starting completion timer for job ${jobId}`);
      setHasStartedCompletionTimer(true);

      // Start timer - don't return cleanup, let it complete
      setTimeout(() => {
        console.log(`[StockTakingJobStatusTracker] Timer fired! Calling onJobCompleted for job ${jobId}`);
        onJobCompleted();
      }, 5000); // Keep completed jobs visible for 5 seconds
    }
  }, [jobStatus, isCompleted, status, hasStartedCompletionTimer, onJobCompleted, jobId]);

  const getStatusIcon = () => {
    if (isLoading) return <Loader2 className="w-4 h-4 animate-spin text-blue-500" />;
    
    if (jobStatus?.isSucceeded) {
      return <CheckCircle className="w-4 h-4 text-green-500" />;
    }
    
    if (jobStatus?.isFailed) {
      return <XCircle className="w-4 h-4 text-red-500" />;
    }
    
    if (isCompleted) {
      return <CheckCircle className="w-4 h-4 text-green-500" />;
    }
    
    // Job is running or pending
    return <Clock className="w-4 h-4 text-yellow-500" />;
  };

  const getStatusText = () => {
    if (jobStatus?.isSucceeded) {
      return "Dokončeno";
    }
    
    if (jobStatus?.isFailed) {
      return "Chyba";
    }
    
    if (isCompleted) {
      return "Dokončeno";
    }
    
    // Map backend status to Czech text
    switch (status) {
      case "Processing":
        return "Zpracovává se";
      case "Enqueued":
        return "Ve frontě";
      case "Succeeded":
        return "Dokončeno";
      case "Failed":
        return "Chyba";
      default:
        return "Ve frontě";
    }
  };

  const getStatusColor = () => {
    if (jobStatus?.isSucceeded || isCompleted) {
      return "text-green-700";
    }
    
    if (jobStatus?.isFailed) {
      return "text-red-700";
    }
    
    switch (status) {
      case "Processing":
        return "text-blue-700";
      case "Enqueued":
        return "text-yellow-700";
      case "Succeeded":
        return "text-green-700";
      case "Failed":
        return "text-red-700";
      default:
        return "text-yellow-700";
    }
  };

  if (error) {
    return (
      <div className="flex items-center space-x-2 text-sm">
        <XCircle className="w-4 h-4 text-red-500" />
        <span className="text-red-700">Chyba načítání statusu</span>
        <span className="text-gray-500 text-xs">({productCode})</span>
      </div>
    );
  }

  return (
    <div className="flex items-center space-x-2 text-sm">
      {getStatusIcon()}
      <span className={getStatusColor()}>{getStatusText()}</span>
      <span className="text-gray-900 text-sm font-semibold">
        Inventarizace: {productCode}
      </span>
      {jobStatus?.errorMessage && (
        <span className="text-red-600 text-xs">({jobStatus.errorMessage})</span>
      )}
    </div>
  );
};

export default StockTakingJobStatusTracker;