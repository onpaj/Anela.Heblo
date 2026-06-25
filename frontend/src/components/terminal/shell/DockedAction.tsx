import React from 'react';
import { Loader2 } from 'lucide-react';
import type { DockAction } from './types';

const VARIANT: Record<NonNullable<DockAction['variant']>, string> = {
  primary: 'bg-primary-blue text-white hover:bg-accent-blue-bright disabled:bg-gray-200 dark:disabled:bg-graphite-surface-2 disabled:text-neutral-gray dark:disabled:text-graphite-faint',
  success: 'bg-success text-white hover:opacity-90 disabled:bg-gray-200 dark:disabled:bg-graphite-surface-2 disabled:text-neutral-gray dark:disabled:text-graphite-faint',
  danger: 'bg-error text-white hover:opacity-90 disabled:bg-gray-200 dark:disabled:bg-graphite-surface-2 disabled:text-neutral-gray dark:disabled:text-graphite-faint',
  ghost: 'border border-border-light dark:border-graphite-border text-neutral-slate dark:text-graphite-text hover:border-primary-blue dark:hover:border-graphite-accent',
};

interface DockedActionProps { actions: DockAction[]; }

export const DockedAction: React.FC<DockedActionProps> = ({ actions }) => {
  if (!actions.length) return null;
  return (
    <div className="flex gap-3 px-4 py-3 bg-white dark:bg-graphite-surface border-t border-border-light dark:border-graphite-border">
      {actions.map((a, i) => (
        <button
          key={i}
          type="button"
          data-testid={a.testId}
          onClick={a.onClick}
          disabled={a.disabled || a.loading}
          className={`flex-1 h-14 flex items-center justify-center gap-2 rounded-xl font-semibold transition-colors disabled:cursor-not-allowed ${VARIANT[a.variant ?? 'primary']}`}
        >
          {a.loading ? <Loader2 className="h-5 w-5 animate-spin" /> : a.icon}
          {a.label}
        </button>
      ))}
    </div>
  );
};
