import React from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";

interface CalendarNavigationProps {
  onPrevious: () => void;
  onNext: () => void;
  onToday?: () => void;
  currentPeriodLabel: string;
  previousTitle?: string;
  nextTitle?: string;
  todayTitle?: string;
  size?: "sm" | "md" | "lg";
  className?: string;
}

const CalendarNavigation: React.FC<CalendarNavigationProps> = ({
  onPrevious,
  onNext,
  onToday,
  currentPeriodLabel,
  previousTitle = "Předchozí",
  nextTitle = "Následující",
  todayTitle = "Dnes",
  size = "md",
  className = "",
}) => {
  const sizeClasses = {
    sm: {
      button: "p-1",
      icon: "h-3 w-3",
      text: "text-xs",
      todayBtn: "px-2 py-1 text-xs",
      label: "text-sm min-w-[160px]",
    },
    md: {
      button: "p-2",
      icon: "h-4 w-4",
      text: "text-sm",
      todayBtn: "px-3 py-1 text-sm",
      label: "text-lg min-w-[180px]",
    },
    lg: {
      button: "p-3",
      icon: "h-5 w-5",
      text: "text-base",
      todayBtn: "px-4 py-2 text-base",
      label: "text-xl min-w-[200px]",
    },
  };

  const classes = sizeClasses[size];

  return (
    <div className={`flex items-center justify-center space-x-4 ${className}`}>
      <button
        onClick={onPrevious}
        className={`${classes.button} hover:bg-gray-100 rounded-lg transition-colors`}
        title={previousTitle}
      >
        <ChevronLeft className={`${classes.icon} text-gray-600`} />
      </button>

      {onToday && (
        <button
          onClick={onToday}
          className={`${classes.todayBtn} font-medium text-indigo-600 hover:bg-indigo-50 rounded transition-colors`}
          title={todayTitle}
        >
          {todayTitle}
        </button>
      )}

      <span className={`${classes.label} ${classes.text} font-medium text-gray-900 text-center`}>
        {currentPeriodLabel}
      </span>

      <button
        onClick={onNext}
        className={`${classes.button} hover:bg-gray-100 rounded-lg transition-colors`}
        title={nextTitle}
      >
        <ChevronRight className={`${classes.icon} text-gray-600`} />
      </button>
    </div>
  );
};

export default CalendarNavigation;