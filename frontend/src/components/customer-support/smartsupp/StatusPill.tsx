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
        className: "bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200",
      };
    case "pending":
      return {
        label: "Čeká",
        className: "bg-amber-50 text-amber-700 ring-1 ring-amber-200",
      };
    case "closed":
    case "resolved":
      return {
        label: "Vyřešeno",
        className: "bg-slate-100 text-slate-600 ring-1 ring-slate-200",
      };
    default:
      return {
        label: status,
        className: "bg-slate-100 text-slate-600 ring-1 ring-slate-200",
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
