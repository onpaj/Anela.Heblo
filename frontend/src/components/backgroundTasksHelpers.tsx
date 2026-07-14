import React from "react";
import { RefreshTaskDto } from "../api/generated/api-client";
import { RefreshCw, CheckCircle, XCircle } from "lucide-react";

export function formatDuration(timeSpan: string): string {
  // TimeSpan format: "hh:mm:ss" or "dd.hh:mm:ss"
  const parts = timeSpan.split(":");
  const firstSegment = parts[0];
  const minutes = parseInt(parts[1]);

  let days = 0;
  let hours: number;
  if (firstSegment.includes(".")) {
    const [dayPart, hourPart] = firstSegment.split(".");
    days = parseInt(dayPart);
    hours = parseInt(hourPart);
  } else {
    hours = parseInt(firstSegment);
  }

  if (days > 0) {
    return `${days}d ${hours}h`;
  } else if (hours > 0) {
    return `${hours}h ${minutes}m`;
  } else {
    return `${minutes}m`;
  }
}

export function getTimeUntilNextRun(nextScheduledRun: Date | string | undefined | null): string {
  if (!nextScheduledRun) return "N/A";

  const now = new Date();
  const nextRun = typeof nextScheduledRun === 'string' ? new Date(nextScheduledRun) : nextScheduledRun;
  const diffMs = nextRun.getTime() - now.getTime();

  if (diffMs < 0) {
    return "Spouští se...";
  }

  const diffMinutes = Math.floor(diffMs / 60000);
  if (diffMinutes < 60) {
    return `za ${diffMinutes} min`;
  }

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) {
    return `za ${diffHours}h ${diffMinutes % 60}m`;
  }

  const diffDays = Math.floor(diffHours / 24);
  return `za ${diffDays}d ${diffHours % 24}h`;
}

export function getStatusBadge(task: RefreshTaskDto) {
  if (!task.enabled) {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-800 dark:text-graphite-muted">
        Vypnuto
      </span>
    );
  }

  if (!task.lastExecution) {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-300">
        Čeká
      </span>
    );
  }

  const status = task.lastExecution.status;

  if (status === "Running") {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 dark:bg-amber-900/30 text-yellow-800 dark:text-amber-300">
        <RefreshCw className="w-3 h-3 mr-1 animate-spin" />
        Běží
      </span>
    );
  }

  if (status === "Completed") {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-100 dark:bg-emerald-900/30 text-emerald-800 dark:text-emerald-300">
        <CheckCircle className="w-3 h-3 mr-1" />
        Úspěch
      </span>
    );
  }

  if (status === "Failed") {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300">
        <XCircle className="w-3 h-3 mr-1" />
        Chyba
      </span>
    );
  }

  if (status === "Cancelled") {
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-graphite-surface-2 text-gray-800 dark:text-graphite-muted">
        Zrušeno
      </span>
    );
  }

  return null;
}
