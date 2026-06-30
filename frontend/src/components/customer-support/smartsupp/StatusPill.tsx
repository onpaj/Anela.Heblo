import React from "react";

interface StatusPillProps {
  status: string;
}

interface PillStyle {
  label: string;
  className: string;
}

function resolvePill(status: string): PillStyle {
  switch (status.toLowerCase()) {
    case "open":
      return {
        label: "Aktivní",
        className: "bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 ring-1 ring-emerald-200 dark:ring-emerald-900/40",
      };
    case "pending":
      return {
        label: "Čeká",
        className: "bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 ring-1 ring-amber-200 dark:ring-amber-900/40",
      };
    case "closed":
    case "resolved":
      return {
        label: "Vyřešeno",
        className: "bg-slate-100 dark:bg-graphite-surface-2 text-slate-600 dark:text-graphite-muted ring-1 ring-slate-200 dark:ring-graphite-border",
      };
    default:
      return {
        label: status,
        className: "bg-slate-100 dark:bg-graphite-surface-2 text-slate-600 dark:text-graphite-muted ring-1 ring-slate-200 dark:ring-graphite-border",
      };
  }
}

const StatusPill: React.FC<StatusPillProps> = ({ status }) => {
  const { label, className } = resolvePill(status);
  return (
    <span
      data-testid="status-pill"
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${className}`}
    >
      {label}
    </span>
  );
};

export default StatusPill;
