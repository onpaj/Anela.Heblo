import React from "react";
import { useRunningInvoiceImportJobs } from "../../api/hooks/useAsyncInvoiceImport";

interface InvoiceImportRunningIndicatorProps {
  className?: string;
}

const InvoiceImportRunningIndicator: React.FC<InvoiceImportRunningIndicatorProps> = ({ 
  className = "" 
}) => {
  const { data: runningJobs } = useRunningInvoiceImportJobs();

  if (!runningJobs || runningJobs.length === 0) {
    return null;
  }

  const runningJobsCount = runningJobs.length;

  return (
    <div className={`relative ${className}`}>
      <div
        className="w-2 h-2 bg-red-500 rounded-full animate-pulse"
        title={`${runningJobsCount} běžící import${runningJobsCount === 1 ? '' : 'y'} faktur`}
      />
      {runningJobsCount > 1 && (
        <div className="absolute -top-1 -right-1 bg-red-600 text-white text-xs rounded-full w-3 h-3 flex items-center justify-center font-bold">
          {runningJobsCount}
        </div>
      )}
    </div>
  );
};

export default InvoiceImportRunningIndicator;