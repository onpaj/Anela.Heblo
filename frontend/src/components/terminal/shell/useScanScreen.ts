// shell/useScanScreen.ts
import { useContext, useEffect, useRef } from 'react';
import { ScanActionsContext } from './ScanProvider';
import type { ScanActions } from './types';

interface UseScanScreenOptions {
  onScan: (code: string) => void;
}

/** Register the calling screen's scan handler for as long as it is mounted. */
export function useScanScreen({ onScan }: UseScanScreenOptions): ScanActions {
  const actions = useContext(ScanActionsContext);
  if (!actions) throw new Error('useScanScreen must be used within a ScanProvider');

  // Keep the latest handler without re-registering every render.
  const handlerRef = useRef(onScan);
  handlerRef.current = onScan;

  useEffect(() => {
    const unregister = actions.registerScanHandler((code) => handlerRef.current(code));
    return unregister;
  }, [actions]);

  return actions;
}
