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
      className="bg-white border border-border-light rounded-xl p-6 flex flex-col items-center text-center gap-2"
    >
      <AlertCircle className="h-10 w-10 text-neutral-gray" />
      <p className="font-semibold text-neutral-slate">{message}</p>
      {hint && <p className="text-sm text-neutral-gray">{hint}</p>}
    </div>
  );
};

export default TerminalError;
