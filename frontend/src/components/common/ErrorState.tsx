import React from "react";
import { AlertCircle } from "lucide-react";

interface ErrorStateProps {
  message: string;
  className?: string;
}

const ErrorState: React.FC<ErrorStateProps> = ({
  message,
  className = "h-64",
}) => {
  return (
    <div className={`flex items-center justify-center ${className}`}>
      <div className="flex items-center space-x-2 text-red-600">
        <AlertCircle className="h-5 w-5" />
        <div>{message}</div>
      </div>
    </div>
  );
};

export default ErrorState;