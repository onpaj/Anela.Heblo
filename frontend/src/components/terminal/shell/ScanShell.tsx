// shell/ScanShell.tsx
import React from 'react';
import { DockedAction } from './DockedAction';
import type { ScanShellProps } from './types';

/** Zones B, C, E. Zone A (app bar) is TerminalLayout. Scan feedback is on-screen (subject +
 *  body), so there is no full-bleed flash overlay or scan strip. */
export const ScanShell: React.FC<ScanShellProps> = ({ subject, children, actions = [] }) => {
  return (
    <div className="flex flex-col h-full min-h-0">
      {/* Zone B — subject header (or empty prompt) */}
      {subject}
      {/* Zone C — workflow body, the only scrolling region */}
      <div className="flex-1 min-h-0 overflow-y-auto">
        <div className="max-w-md mx-auto w-full p-4">{children}</div>
      </div>
      {/* Zone E — docked action */}
      <DockedAction actions={actions} />
    </div>
  );
};
