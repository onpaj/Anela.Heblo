import React from 'react';
import { AlertCircle } from 'lucide-react';

interface TerminalErrorProps {
  message: string;
  hint?: string;
}

const TerminalError: React.FC<TerminalErrorProps> = ({ message, hint }) => {
  return (
    <div
      data-testid="terminal-error"
      className="bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-6 flex flex-col items-center text-center gap-2"
    >
      <AlertCircle className="h-10 w-10 text-neutral-gray dark:text-graphite-muted" />
      <p className="font-semibold text-neutral-slate dark:text-graphite-text">{message}</p>
      {hint && <p className="text-sm text-neutral-gray dark:text-graphite-muted">{hint}</p>}
    </div>
  );
};

export default TerminalError;
