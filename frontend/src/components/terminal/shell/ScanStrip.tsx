import React, { useContext } from 'react';
import { ScanLine } from 'lucide-react';
import { ScanEchoContext } from './ScanProvider';
import type { FlashTone } from './types';

const TONE_TEXT: Record<FlashTone, string> = {
  ok: 'text-success', warn: 'text-warning', err: 'text-error',
};

/** Persistent wedge surface. Subscribes only to the volatile echo context, so it
 *  re-renders on keystrokes without re-rendering workflow bodies. */
export const ScanStrip: React.FC = () => {
  const { buffer, lastCode, lastTone } = useContext(ScanEchoContext);

  return (
    <div data-testid="scan-strip"
         className="flex items-center gap-3 px-4 py-3 bg-neutral-slate text-white">
      <ScanLine className="h-5 w-5 text-scan-accent flex-shrink-0" />
      {buffer ? (
        <span className="font-mono text-base">
          {buffer}<span className="animate-pulse">▌</span>
        </span>
      ) : lastCode ? (
        <span className={`font-mono text-base ${lastTone ? TONE_TEXT[lastTone] : 'text-white'}`}>
          {lastCode}
        </span>
      ) : (
        <span className="text-sm text-white/70">Připraveno ke skenování…</span>
      )}
    </div>
  );
};
