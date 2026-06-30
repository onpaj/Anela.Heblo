import React from "react";
import { Loader2 } from "lucide-react";

interface LoadingIndicatorProps {
  isVisible: boolean;
}

export const LoadingIndicator: React.FC<LoadingIndicatorProps> = ({
  isVisible,
}) => {
  if (!isVisible) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 bg-white dark:bg-graphite-surface shadow-lg dark:shadow-soft-dark rounded-full p-3 border border-gray-200 dark:border-graphite-border">
      <Loader2 className="h-5 w-5 animate-spin text-indigo-600" />
    </div>
  );
};
