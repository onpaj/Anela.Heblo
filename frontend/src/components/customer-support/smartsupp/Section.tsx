import React from "react";

interface SectionProps {
  title: string;
  children: React.ReactNode;
}

function Section({ title, children }: SectionProps) {
  return (
    <div className="px-4 py-3 border-b border-gray-100 dark:border-graphite-border">
      <div className="text-[11px] uppercase tracking-wide text-gray-400 dark:text-graphite-faint font-medium mb-1.5">{title}</div>
      {children}
    </div>
  );
}

export default Section;
