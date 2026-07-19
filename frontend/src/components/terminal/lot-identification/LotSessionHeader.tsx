import { type ReactNode } from 'react';
import { type LucideIcon, ScanLine } from 'lucide-react';

interface LotSessionHeaderProps {
  /** session title, e.g. "Volný příjem" or a purchase-order number */
  title: string;
  /** key facts line — supplier, selected material, running count… */
  facts?: ReactNode;
  /** monospace code (e.g. the selected material code) shown next to the title */
  code?: string;
  icon?: LucideIcon;
  /** left-accent colour; defaults to the lot-identification violet from the prototype */
  accent?: string;
  testId?: string;
}

const ACCENT = '#7C3AED';

/**
 * Zone B for the lot-identification session screens. Unlike SubjectHeader (which is box-code +
 * state-badge specific) this renders a standing "session" context: a titled operation with an
 * icon tile and a facts line, matching the prototype's subject header.
 */
const LotSessionHeader = ({
  title,
  facts,
  code,
  icon: Icon = ScanLine,
  accent = ACCENT,
  testId = 'lot-session-header',
}: LotSessionHeaderProps) => (
  <div
    data-testid={testId}
    style={{ borderLeftColor: accent }}
    className="flex items-center gap-3 border-l-4 border-b border-border-light dark:border-graphite-border bg-white dark:bg-graphite-surface px-4 py-3"
  >
    <div
      style={{ color: accent }}
      className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-secondary-blue-pale dark:bg-graphite-surface-2"
    >
      <Icon className="h-5 w-5" />
    </div>
    <div className="min-w-0 flex-1">
      <div className="flex items-baseline gap-2">
        <span className="truncate text-base font-semibold text-neutral-slate dark:text-graphite-text">
          {title}
        </span>
        {code && (
          <span className="font-mono text-sm text-neutral-gray dark:text-graphite-muted">{code}</span>
        )}
      </div>
      {facts && (
        <div className="truncate text-sm text-neutral-gray dark:text-graphite-muted">{facts}</div>
      )}
    </div>
  </div>
);

export default LotSessionHeader;
