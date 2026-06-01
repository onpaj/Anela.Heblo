// shell/ScanProvider.tsx
import React, {
  createContext, useCallback, useEffect, useMemo, useRef, useState, type ReactNode,
} from 'react';
import type { FlashState, FlashTone, ScanActions, ScanEcho } from './types';

export const ScanActionsContext = createContext<ScanActions | null>(null);
export const ScanEchoContext = createContext<ScanEcho>({ buffer: '', lastCode: null, lastTone: null });
export const FlashContext = createContext<FlashState | null>(null);

const REFOCUS_DELAY_MS = 100;
const SAFETY_REFOCUS_MS = 2000;

/** A terminator confirms the buffer. DataWedge default = Enter; Tab kept as a guard. */
function isTerminator(key: string): boolean {
  return key === 'Enter' || key === 'Tab';
}

export const ScanProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const inputRef = useRef<HTMLInputElement>(null);
  const handlerRef = useRef<((code: string) => void) | null>(null);
  const yieldRef = useRef(false);
  const flashId = useRef(0);

  const [echo, setEcho] = useState<ScanEcho>({ buffer: '', lastCode: null, lastTone: null });
  const [flash, setFlash] = useState<FlashState | null>(null);

  const focusWedge = useCallback(() => {
    if (yieldRef.current) return;
    const active = document.activeElement;
    // Never steal focus from a real input/textarea (e.g. an open sheet field).
    if (active && active !== inputRef.current &&
        (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA')) return;
    inputRef.current?.focus({ preventScroll: true });
  }, []);

  // Refocus on mount + low-frequency safety interval.
  useEffect(() => {
    focusWedge();
    const id = window.setInterval(focusWedge, SAFETY_REFOCUS_MS);
    return () => window.clearInterval(id);
  }, [focusWedge]);

  const handleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setEcho((prev) => ({ ...prev, buffer: e.target.value }));
  }, []);

  const handleKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isTerminator(e.key)) return;
    e.preventDefault();
    const code = inputRef.current?.value.trim().toUpperCase() ?? '';
    if (inputRef.current) inputRef.current.value = '';
    setEcho((prev) => ({ ...prev, buffer: '' }));
    if (!code) return;
    setEcho((prev) => ({ ...prev, lastCode: code }));
    handlerRef.current?.(code);
  }, []);

  const handleBlur = useCallback(() => {
    window.setTimeout(focusWedge, REFOCUS_DELAY_MS);
  }, [focusWedge]);

  const actions = useMemo<ScanActions>(() => ({
    registerScanHandler: (handler) => {
      handlerRef.current = handler;
      return () => { if (handlerRef.current === handler) handlerRef.current = null; };
    },
    flash: (tone: FlashTone, code?: string) => {
      flashId.current += 1;
      setFlash({ id: flashId.current, tone, code });
      setEcho((prev) => ({ ...prev, lastTone: tone, lastCode: code ?? prev.lastCode }));
    },
    setYieldFocus: (shouldYield: boolean) => {
      yieldRef.current = shouldYield;
      if (!shouldYield) window.setTimeout(focusWedge, REFOCUS_DELAY_MS);
    },
    refocus: () => window.setTimeout(focusWedge, REFOCUS_DELAY_MS),
  }), [focusWedge]);

  return (
    <ScanActionsContext.Provider value={actions}>
      <ScanEchoContext.Provider value={echo}>
        <FlashContext.Provider value={flash}>
          {/* Singleton hidden wedge: captures keystrokes, no soft keyboard, no caret, off-screen. */}
          <input
            ref={inputRef}
            data-testid="wedge-input"
            inputMode="none"
            autoComplete="off"
            autoCapitalize="off"
            aria-hidden="true"
            tabIndex={-1}
            onChange={handleChange}
            onKeyDown={handleKeyDown}
            onBlur={handleBlur}
            className="absolute opacity-0 h-px w-px -z-10 pointer-events-none"
          />
          <div onClick={() => focusWedge()}>{children}</div>
        </FlashContext.Provider>
      </ScanEchoContext.Provider>
    </ScanActionsContext.Provider>
  );
};
