import React from "react";
import { useNavigate } from "react-router-dom";
import { Loader2, AlertTriangle, ChevronRight } from "lucide-react";
import { GetStockUpOperationsSummaryResponse } from "../../../api/generated/api-client";

interface StockUpOperationStatusIndicatorProps {
  summary: GetStockUpOperationsSummaryResponse;
}

const StockUpOperationStatusIndicator: React.FC<
  StockUpOperationStatusIndicatorProps
> = ({ summary }) => {
  const navigate = useNavigate();

  const handleClick = () => {
    // Navigate to stock-up operations page with filters
    navigate(
      "/stock-up-operations?sourceType=GiftPackageManufacture&state=Pending,Submitted,Failed"
    );
  };

  // Don't render if there are no operations to show
  if ((summary.totalInQueue ?? 0) === 0 && (summary.failedCount ?? 0) === 0) {
    return null;
  }

  return (
    <div
      className="mb-4 p-4 bg-blue-50 rounded-lg border border-blue-200 cursor-pointer hover:bg-blue-100 transition-colors focus:ring-2 focus:ring-blue-500 focus:outline-none"
      onClick={handleClick}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          handleClick();
        }
      }}
      role="button"
      tabIndex={0}
      aria-label="Zobrazit stock-up operace s filtry"
      data-testid="stockup-status-indicator"
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center space-x-4">
          {(summary.totalInQueue ?? 0) > 0 && (
            <div className="flex items-center space-x-2" data-testid="queue-indicator">
              <Loader2 className="h-5 w-5 text-blue-600 animate-spin" />
              <span className="text-sm font-medium text-blue-900">
                {summary.totalInQueue} operací ve frontě
              </span>
            </div>
          )}

          {(summary.failedCount ?? 0) > 0 && (
            <div className="flex items-center space-x-2" data-testid="failed-indicator">
              <AlertTriangle className="h-5 w-5 text-red-600" />
              <span className="text-sm font-medium text-red-900">
                {summary.failedCount} selhalo
              </span>
            </div>
          )}
        </div>

        <ChevronRight className="h-5 w-5 text-gray-400" />
      </div>
    </div>
  );
};

export default StockUpOperationStatusIndicator;
