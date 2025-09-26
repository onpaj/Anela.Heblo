import React from "react";
import { Loader2 } from "lucide-react";

interface LoadingStateProps {
  message?: string;
  className?: string;
}

const LoadingState: React.FC<LoadingStateProps> = ({
  message = "Načítání...",
  className = "h-64",
}) => {
  return (
    <div className={`flex items-center justify-center ${className}`}>
      <div className="flex items-center space-x-2">
        <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
        <div className="text-gray-500">{message}</div>
      </div>
    </div>
  );
};

export default LoadingState;