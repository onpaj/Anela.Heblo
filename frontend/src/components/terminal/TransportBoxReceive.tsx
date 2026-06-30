import React, { useEffect, useState } from 'react';
import { Loader2, Check, X } from 'lucide-react';
import BoxDetail from './TransportBoxDetail';
import {
  useTransportBoxByCodeQuery,
  useChangeTransportBoxState,
} from '../../api/hooks/useTransportBoxes';
import { TransportBoxState, SwaggerException } from '../../api/generated/api-client';
import { ScanShell } from './shell/ScanShell';
import { SubjectHeader } from './shell/SubjectHeader';
import { useScanScreen } from './shell/useScanScreen';
import type { DockAction } from './shell/types';
import { useScreenView } from '../../telemetry/useScreenView';

const TransportBoxReceive: React.FC = () => {
  useScreenView('Terminal', 'TerminalReceive');
  const [scannedCode, setScannedCode] = useState<string | null>(null);

  const { data: box, isFetching, isError, refetch } = useTransportBoxByCodeQuery(scannedCode);
  const changeState = useChangeTransportBoxState();
  const canReceive = box?.isReceivable === true;

  const { flash } = useScanScreen({
    onScan: (code) => {
      if (box?.isReceivable === true && box.code?.toUpperCase() === code && !changeState.isPending) {
        // eslint-disable-next-line @typescript-eslint/no-use-before-define
        void handleAccept(); return;
      }
      if (isError && scannedCode === code) { void refetch(); return; }
      setScannedCode(code);
    },
  });

  // One flash per resolved scan (skip while a box is in hand and we auto-confirm).
  useEffect(() => {
    if (!scannedCode || isFetching) return;
    if (isError) flash('err', scannedCode);
    else if (!box) flash('err', scannedCode);
    else if (!box.isReceivable) flash('warn', box.code ?? scannedCode);
    else flash('ok', box.code ?? scannedCode);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scannedCode, isFetching, isError, box]);

  const handleAccept = async () => {
    if (!box || box.isReceivable !== true || changeState.isPending || !box.id) return;
    try {
      await changeState.mutateAsync({ boxId: box.id, newState: TransportBoxState.Received });
      flash('ok', box.code ?? undefined);
      setScannedCode(null);
    } catch (error: unknown) {
      if (!(error instanceof SwaggerException)) throw error;
      flash('err', box.code ?? undefined);
    }
  };

  const handleReject = () => setScannedCode(null);

  const subject = box
    ? <SubjectHeader code={box.code} state={box.state}
        facts={canReceive ? 'Připraveno k příjmu' : 'Nelze přijmout v tomto stavu'} />
    : <SubjectHeader emptyPrompt="Naskenujte box k příjmu" />;

  const actions: DockAction[] = box ? [
    { label: 'Zamítnout', variant: 'ghost', onClick: handleReject, testId: 'reject-box', icon: <X className="h-5 w-5" /> },
    { label: canReceive ? 'Potvrdit příjem' : 'Nelze přijmout', variant: 'success',
      onClick: handleAccept, disabled: !canReceive || changeState.isPending,
      loading: changeState.isPending, testId: 'accept-box', icon: <Check className="h-5 w-5" /> },
  ] : [];

  return (
    <ScanShell subject={subject} actions={actions}>
      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}
      {!canReceive && box && (
        <div data-testid="not-receivable"
             className="bg-error-pale dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-xl p-3 text-sm text-red-700 dark:text-red-300 mb-3">
          Tento box nelze přijmout. Pro příjem musí být ve stavu V přepravě, V rezervě nebo V karanténě.
        </div>
      )}
      {!isFetching && box && <BoxDetail box={box} />}
    </ScanShell>
  );
};

export default TransportBoxReceive;
