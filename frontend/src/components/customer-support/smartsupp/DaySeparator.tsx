import React from "react";

interface DaySeparatorProps {
  date: string;
}

function formatDayLabel(dateStr: string): string {
  const target = new Date(dateStr);
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);

  const sameDay = (a: Date, b: Date) =>
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate();

  if (sameDay(target, today)) return "Dnes";
  if (sameDay(target, yesterday)) return "Včera";
  return target.toLocaleDateString("cs-CZ", { day: "numeric", month: "long", year: "numeric" });
}

const DaySeparator: React.FC<DaySeparatorProps> = ({ date }) => (
  <div className="flex items-center my-3" data-testid="day-separator">
    <div className="flex-1 h-px bg-gray-200 dark:bg-graphite-border" />
    <span className="mx-3 text-xs text-gray-400 dark:text-graphite-faint font-medium">{formatDayLabel(date)}</span>
    <div className="flex-1 h-px bg-gray-200 dark:bg-graphite-border" />
  </div>
);

export default DaySeparator;
