import React from 'react';

export interface AdsTab<T extends string> {
  id: T;
  label: string;
}

interface AdsTabsProps<T extends string> {
  tabs: ReadonlyArray<AdsTab<T>>;
  active: T;
  onChange: (tab: T) => void;
}

const ACTIVE_TAB = 'border-indigo-600 text-indigo-600 dark:border-graphite-accent dark:text-graphite-accent';
const INACTIVE_TAB = 'border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700';

function AdsTabs<T extends string>({ tabs, active, onChange }: AdsTabsProps<T>) {
  return (
    <div role="tablist" className="flex gap-1 border-b border-gray-200 dark:border-graphite-border overflow-x-auto">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={tab.id === active}
          onClick={() => onChange(tab.id)}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px whitespace-nowrap ${tab.id === active ? ACTIVE_TAB : INACTIVE_TAB}`}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}

export default AdsTabs;
