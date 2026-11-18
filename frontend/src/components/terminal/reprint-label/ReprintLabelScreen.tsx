import React, { useRef, useState } from 'react';
import { ScanShell } from '../shell/ScanShell';
import { SubjectHeader } from '../shell/SubjectHeader';
import { BottomSheet } from '../shell/BottomSheet';
import { useScanScreen } from '../shell/useScanScreen';
import { useScreenView } from '../../../telemetry/useScreenView';
import { useReprintMaterialContainerLabel } from '../../../api/hooks/useMaterialContainers';
import { resolveReprintErrorMessage } from '../../../api/hooks/reprintErrors';

const HISTORY_LIMIT = 10;

interface HistoryEntry {
  id: number;
  code: string;
  isSuccess: boolean;
}

const ReprintLabelScreen: React.FC = () => {
  useScreenView('Terminal', 'TerminalReprintLabel');
  const { mutate } = useReprintMaterialContainerLabel();
  const [history, setHistory] = useState<HistoryEntry[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSheetOpen, setIsSheetOpen] = useState(false);

  // Refs, not state: the scan handler must never read a stale value between renders.
  const isInFlightRef = useRef(false);
  const isSheetOpenRef = useRef(false);
  const pendingCodeRef = useRef<string | null>(null);
  const isSessionConfirmedRef = useRef(false);
  const entryIdRef = useRef(0);
  const flashRef = useRef<ReturnType<typeof useScanScreen>['flash'] | null>(null);

  const setSheet = (isOpen: boolean) => {
    isSheetOpenRef.current = isOpen;
    setIsSheetOpen(isOpen);
  };

  const addHistory = (code: string, isSuccess: boolean) => {
    entryIdRef.current += 1;
    const entry: HistoryEntry = { id: entryIdRef.current, code, isSuccess };
    setHistory((prev) => [entry, ...prev].slice(0, HISTORY_LIMIT));
  };

  const print = (code: string) => {
    isInFlightRef.current = true;
    pendingCodeRef.current = code;
    setErrorMessage(null);
    mutate(
      { code, mediaChangeConfirmed: isSessionConfirmedRef.current },
      {
        onSuccess: (response) => {
          isInFlightRef.current = false;
          if (response.requiresMediaChangeConfirmation) {
            setSheet(true);
            return;
          }
          pendingCodeRef.current = null;
          flashRef.current?.('ok', code);
          addHistory(code, true);
        },
        onError: (error) => {
          isInFlightRef.current = false;
          pendingCodeRef.current = null;
          flashRef.current?.('err', code);
          setErrorMessage(resolveReprintErrorMessage(error));
          addHistory(code, false);
        },
      },
    );
  };

  const { flash } = useScanScreen({
    onScan: (raw) => {
      const code = raw.trim().toUpperCase();
      if (!code) return;
      if (isInFlightRef.current || isSheetOpenRef.current) {
        flash('warn', code);
        return;
      }
      print(code);
    },
  });
  flashRef.current = flash;

  const handleConfirm = () => {
    const code = pendingCodeRef.current;
    if (!code) return;
    isSessionConfirmedRef.current = true;
    setSheet(false);
    print(code);
  };

  const handleCancel = () => {
    pendingCodeRef.current = null;
    setSheet(false);
  };

  return (
    <ScanShell subject={<SubjectHeader emptyPrompt="Naskenujte štítek kontejneru" />}>
      <h1 className="text-xl font-bold text-neutral-slate dark:text-graphite-text">Dotisk štítku</h1>
      <p className="text-sm text-neutral-gray dark:text-graphite-muted mt-1">Naskenujte štítek kontejneru</p>
      {errorMessage && (
        <p role="alert" className="mt-3 text-sm font-medium text-red-600 dark:text-red-400">
          {errorMessage}
        </p>
      )}
      <ul data-testid="reprint-history" className="mt-4 space-y-2">
        {history.map((entry) => (
          <li
            key={entry.id}
            className="flex items-center gap-2 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-lg px-3 py-2 text-sm text-neutral-slate dark:text-graphite-text"
          >
            <span className={entry.isSuccess ? 'text-green-600 dark:text-green-400' : 'text-red-600 dark:text-red-400'}>
              {entry.isSuccess ? '✓' : '✗'}
            </span>
            <span className="font-mono">{entry.code}</span>
          </li>
        ))}
      </ul>
      <BottomSheet open={isSheetOpen} onClose={handleCancel} testId="media-change-sheet" ariaLabel="Výměna role">
        <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">
          Vyměňte roli za štítky kontejnerů
        </h2>
        <div className="mt-4 flex flex-col gap-2">
          <button
            type="button"
            onClick={handleConfirm}
            className="w-full min-h-[48px] rounded-lg bg-primary-blue text-white font-semibold"
          >
            Role vyměněna, tisknout
          </button>
          <button
            type="button"
            onClick={handleCancel}
            className="w-full min-h-[48px] rounded-lg border border-border-light dark:border-graphite-border text-neutral-slate dark:text-graphite-text"
          >
            Zrušit
          </button>
        </div>
      </BottomSheet>
    </ScanShell>
  );
};

export default ReprintLabelScreen;
