import React from 'react';
import { CheckCircle, Lock } from 'lucide-react';

export type StepState = 'active' | 'done' | 'pending' | 'locked';

interface StepSectionProps {
  step: number;
  title: string;
  state: StepState;
  onEdit?: () => void;
  children: React.ReactNode;
  testId?: string;
}

const containerClasses: Record<StepState, string> = {
  active:
    'border-primary-blue bg-secondary-blue-pale dark:border-graphite-accent dark:bg-graphite-surface-2',
  done: 'border-border-light dark:border-graphite-border',
  locked: 'border-border-light dark:border-graphite-border',
  pending: 'border-border-light dark:border-graphite-border opacity-50',
};

const Badge = ({ step, state }: { step: number; state: StepState }) => {
  if (state === 'done' || state === 'locked') {
    return <CheckCircle className="h-5 w-5 text-green-600 dark:text-emerald-400" />;
  }
  const filled = state === 'active';
  return (
    <span
      className={`flex h-5 w-5 items-center justify-center rounded-full text-xs font-semibold ${
        filled
          ? 'bg-primary-blue text-white dark:bg-graphite-accent'
          : 'bg-border-light text-neutral-gray dark:bg-graphite-border dark:text-graphite-muted'
      }`}
    >
      {step}
    </span>
  );
};

const StepSection = ({ step, title, state, onEdit, children, testId }: StepSectionProps) => (
  <section
    data-testid={testId}
    className={`rounded-xl border p-3 transition-colors ${containerClasses[state]}`}
  >
    <div className="mb-2 flex items-center gap-2">
      <Badge step={step} state={state} />
      <span className="flex-1 text-sm font-medium text-neutral-slate dark:text-graphite-text">
        {title}
      </span>
      {state === 'locked' && <Lock className="h-4 w-4 text-neutral-gray dark:text-graphite-muted" />}
      {state === 'done' && onEdit && (
        <button
          type="button"
          onClick={onEdit}
          className="text-sm font-medium text-primary-blue dark:text-graphite-accent"
        >
          Upravit
        </button>
      )}
    </div>
    {children}
  </section>
);

export default StepSection;
