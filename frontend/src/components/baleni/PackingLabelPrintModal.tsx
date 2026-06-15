import { useCallback, useEffect, useRef, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { printLabelWithReadiness } from './printLabelPdf';
import { useCompletePackingOrder } from '../../api/hooks/useCompletePackingOrder';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';

interface PackingLabelPrintModalProps {
  order: PackingOrder;
  shipment: ScanShipment;
  onComplete: () => void;
  onCancel: () => void;
}

type Phase = 'printing' | 'awaitingNext' | 'completing' | 'timeout' | 'error';

function PackingLabelPrintModal({ order, shipment, onComplete, onCancel }: PackingLabelPrintModalProps) {
  const totalLabels = shipment.packages.length;
  const [currentLabel, setCurrentLabel] = useState(1);
  const [phase, setPhase] = useState<Phase>('printing');
  const [completionError, setCompletionError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const completedRef = useRef(false);
  const completeMutation = useCompletePackingOrder();

  const complete = useCallback(() => {
    if (completedRef.current) return;
    completedRef.current = true;
    completeMutation.mutate(order.code, {
      onSuccess: () => { onComplete(); },
      onError: (err) => {
        completedRef.current = false;
        setCompletionError(err.message);
      },
    });
  }, [completeMutation, order.code, onComplete]);

  const startPrint = useCallback(async (n: number) => {
    setPhase('printing');
    setCompletionError(null);
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    const result = await printLabelWithReadiness(order.code, n, { signal: controller.signal });

    if (controller.signal.aborted) return;

    if (result.printed) {
      setCurrentLabel(n);
      if (n >= totalLabels) {
        setPhase('completing');
        complete();
      } else {
        setPhase('awaitingNext');
      }
    } else if (result.timedOut) {
      setPhase('timeout');
    } else {
      setPhase('error');
    }
  }, [order.code, totalLabels, complete]);

  useEffect(() => {
    void startPrint(1);
    return () => {
      abortRef.current?.abort();
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const retryCompletion = () => {
    setCompletionError(null);
    complete();
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6"
      data-testid="packing-label-print-modal"
    >
      <div className="flex w-full max-w-md flex-col items-center gap-6 rounded-2xl bg-white p-10 shadow-2xl">
        {phase === 'printing' && (
          <div data-testid="print-modal-printing" className="flex flex-col items-center gap-4">
            <Loader2 className="h-10 w-10 animate-spin text-primary-blue" />
            <p className="text-lg font-medium text-neutral-slate">
              Připravuji štítek {currentLabel}/{totalLabels}…
            </p>
          </div>
        )}

        {phase === 'awaitingNext' && (
          <div data-testid="print-modal-awaiting-next" className="flex flex-col items-center gap-4">
            <p data-testid="print-modal-label-count" className="text-lg font-medium text-neutral-slate">
              Štítek {currentLabel}/{totalLabels}
            </p>
            <button
              type="button"
              data-testid="print-next-label-button"
              onClick={() => void startPrint(currentLabel + 1)}
              className="rounded-xl bg-primary-blue px-8 py-4 text-lg font-semibold text-white shadow active:scale-95"
            >
              Tisknout další štítek
            </button>
          </div>
        )}

        {phase === 'completing' && (
          <div data-testid="print-modal-completing" className="flex flex-col items-center gap-4">
            {!completionError && <Loader2 className="h-10 w-10 animate-spin text-primary-blue" />}
            <p className="text-lg font-medium text-neutral-slate">Dokončuji objednávku…</p>
            {completionError && (
              <div className="flex flex-col items-center gap-3">
                <p className="text-center text-error-red">{completionError}</p>
                <button
                  type="button"
                  data-testid="retry-completion-button"
                  onClick={retryCompletion}
                  className="rounded-xl bg-primary-blue px-8 py-4 text-lg font-semibold text-white shadow active:scale-95"
                >
                  Zkusit znovu
                </button>
              </div>
            )}
          </div>
        )}

        {phase === 'timeout' && (
          <div data-testid="print-modal-timeout" className="flex flex-col items-center gap-4">
            <p className="text-center text-lg font-medium text-neutral-slate">
              Štítek {currentLabel} zatím není připraven u dopravce.
            </p>
            <button
              type="button"
              data-testid="retry-print-button"
              onClick={() => void startPrint(currentLabel)}
              className="rounded-xl bg-primary-blue px-8 py-4 text-lg font-semibold text-white shadow active:scale-95"
            >
              Zkusit znovu
            </button>
          </div>
        )}

        {phase === 'error' && (
          <div data-testid="print-modal-error" className="flex flex-col items-center gap-4">
            <p className="text-center text-lg font-medium text-neutral-slate">
              Nepodařilo se načíst štítek {currentLabel}.
            </p>
            <button
              type="button"
              data-testid="retry-print-button"
              onClick={() => void startPrint(currentLabel)}
              className="rounded-xl bg-primary-blue px-8 py-4 text-lg font-semibold text-white shadow active:scale-95"
            >
              Zkusit znovu
            </button>
          </div>
        )}

        {phase !== 'completing' && (
          <button
            type="button"
            data-testid="cancel-print-modal-button"
            onClick={onCancel}
            className="mt-2 rounded-lg px-6 py-2 text-sm text-neutral-gray hover:bg-neutral-100"
          >
            Zrušit
          </button>
        )}
      </div>
    </div>
  );
}

export default PackingLabelPrintModal;
