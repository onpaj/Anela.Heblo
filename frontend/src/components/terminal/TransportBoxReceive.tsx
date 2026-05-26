import React, { useEffect, useState } from 'react';
import { Loader2, PackageX, Check, X, CheckCircle2, AlertCircle } from 'lucide-react';
import ScanInput from './ScanInput';
import BoxDetail from './TransportBoxDetail';
import {
  useTransportBoxByCodeQuery,
  useChangeTransportBoxState,
} from '../../api/hooks/useTransportBoxes';
import { TransportBoxState, SwaggerException } from '../../api/generated/api-client';
import { useScreenView } from '../../telemetry/useScreenView';

const SUCCESS_DISPLAY_MS = 2500;

const TransportBoxReceive: React.FC = () => {
  useScreenView('Terminal', 'TerminalReceive');
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const [receivedCode, setReceivedCode] = useState<string | null>(null);

  const { data: box, isFetching, isError, refetch } = useTransportBoxByCodeQuery(scannedCode);
  const changeState = useChangeTransportBoxState();

  const showNotFound = !!scannedCode && !isFetching && !isError && !box;
  const showError = !!scannedCode && !isFetching && isError;
  const canReceive = box?.isReceivable === true;

  useEffect(() => {
    if (!receivedCode) return;
    const timer = setTimeout(() => setReceivedCode(null), SUCCESS_DISPLAY_MS);
    return () => clearTimeout(timer);
  }, [receivedCode]);

  const handleAccept = async () => {
    if (!box || box.isReceivable !== true || changeState.isPending || !box.id) return;
    try {
      await changeState.mutateAsync({
        boxId: box.id,
        newState: TransportBoxState.Received,
      });
      setReceivedCode(box.code ?? null);
      setScannedCode(null);
    } catch (error: unknown) {
      if (!(error instanceof SwaggerException)) throw error;
      // SwaggerException is handled by the global toast handler.
    }
  };

  const handleReject = () => {
    setScannedCode(null);
    setReceivedCode(null);
  };

  const handleScan = (value: string) => {
    if (box?.isReceivable === true && box.code?.toUpperCase() === value && !changeState.isPending) {
      void handleAccept();
      return;
    }
    setReceivedCode(null);
    if (isError && scannedCode === value) {
      void refetch();
      return;
    }
    setScannedCode(value);
  };

  return (
    <div className="space-y-4">
      <div className="sticky top-0 z-10 bg-background-gray pb-3">
        <ScanInput
          label="Kód boxu"
          onScan={handleScan}
          loading={changeState.isPending}
          suppressKeyboard
          allowKeyboardToggle
        />
      </div>

      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}

      {receivedCode && !box && (
        <div
          data-testid="receive-success"
          className="bg-green-50 border border-green-200 rounded-xl p-6 flex flex-col items-center text-center gap-2"
        >
          <CheckCircle2 className="h-10 w-10 text-green-600" />
          <p className="font-semibold text-neutral-slate">
            Box {receivedCode} přijat
          </p>
          <p className="text-sm text-neutral-gray">Naskenujte další box</p>
        </div>
      )}

      {showNotFound && (
        <div
          data-testid="box-not-found"
          className="bg-white border border-border-light rounded-xl p-6 flex flex-col items-center text-center gap-2"
        >
          <PackageX className="h-10 w-10 text-neutral-gray" />
          <p className="font-semibold text-neutral-slate">
            Box {scannedCode} nenalezen
          </p>
          <p className="text-sm text-neutral-gray">
            Zkontrolujte kód a naskenujte znovu
          </p>
        </div>
      )}

      {showError && (
        <div
          data-testid="box-load-error"
          className="bg-white border border-red-200 rounded-xl p-6 flex flex-col items-center text-center gap-2"
        >
          <AlertCircle className="h-10 w-10 text-red-500" />
          <p className="font-semibold text-neutral-slate">
            Chyba při načítání boxu {scannedCode}
          </p>
          <p className="text-sm text-neutral-gray">
            Zkuste naskenovat znovu
          </p>
        </div>
      )}

      {!isFetching && box && (
        <div className="space-y-3">
          <BoxDetail box={box} />

          {!canReceive && (
            <div
              data-testid="not-receivable"
              className="bg-red-50 border border-red-200 rounded-xl p-3 text-sm text-red-700"
            >
              Tento box nelze přijmout. Pro příjem musí být ve stavu V přepravě,
              V rezervě nebo V karanténě.
            </div>
          )}

          <div className="flex gap-3 pt-1">
            <button
              type="button"
              data-testid="reject-box"
              onClick={handleReject}
              className="flex-1 h-14 flex items-center justify-center gap-2 rounded-xl border border-border-light text-neutral-slate font-semibold hover:border-primary-blue transition-colors"
            >
              <X className="h-5 w-5" />
              Zamítnout
            </button>
            <button
              type="button"
              data-testid="accept-box"
              onClick={handleAccept}
              disabled={!canReceive || changeState.isPending}
              className="flex-1 h-14 flex items-center justify-center gap-2 rounded-xl bg-green-600 text-white font-semibold hover:bg-green-700 transition-colors disabled:bg-gray-200 disabled:text-neutral-gray disabled:cursor-not-allowed"
            >
              {changeState.isPending ? (
                <Loader2 className="h-5 w-5 animate-spin" />
              ) : (
                <Check className="h-5 w-5" />
              )}
              {canReceive ? 'Potvrdit příjem' : 'Nelze přijmout'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default TransportBoxReceive;
