import React, { useContext, useEffect, useState } from 'react';
import { Check, AlertTriangle, X } from 'lucide-react';
import { FlashContext } from './ScanProvider';
import type { FlashTone } from './types';

const TONE_MS: Record<FlashTone, number> = { ok: 950, warn: 950, err: 1200 };
const TONE_BG: Record<FlashTone, string> = {
  ok: 'bg-success', warn: 'bg-warning', err: 'bg-error',
};
const ToneGlyph: Record<FlashTone, React.ReactNode> = {
  ok: <Check className="h-24 w-24 text-white" />,
  warn: <AlertTriangle className="h-24 w-24 text-white" />,
  err: <X className="h-24 w-24 text-white" />,
};

/** Full-bleed colour wash on each scan. Visible-by-default (backgrounded tabs
 *  freeze animations); the fade is polish only, never a visibility gate. */
export const FlashOverlay: React.FC = () => {
  const flash = useContext(FlashContext);
  const [active, setActive] = useState<typeof flash>(null);

  useEffect(() => {
    if (!flash) return;
    setActive(flash);
    const id = window.setTimeout(() => setActive(null), TONE_MS[flash.tone]);
    return () => window.clearTimeout(id); // new flash cancels the prior timer
  }, [flash]);

  if (!active) return null;

  return (
    <div
      data-testid="flash-overlay"
      data-tone={active.tone}
      aria-live="assertive"
      role="status"
      className={`fixed inset-0 z-50 flex items-center justify-center pointer-events-none ${TONE_BG[active.tone]} opacity-90 transition-opacity duration-200`}
    >
      {ToneGlyph[active.tone]}
    </div>
  );
};
