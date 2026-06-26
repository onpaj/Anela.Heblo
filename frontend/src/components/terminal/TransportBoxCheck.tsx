import React, { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useTransportBoxByCodeQuery } from '../../api/hooks/useTransportBoxes';
import TerminalError from './TerminalError';
import { getTerminalErrorMessage } from './terminalErrors';
import BoxDetail from './TransportBoxDetail';
import { ScanShell } from './shell/ScanShell';
import { SubjectHeader } from './shell/SubjectHeader';
import { useScanScreen } from './shell/useScanScreen';
import { useScreenView } from '../../telemetry/useScreenView';

const TransportBoxCheck: React.FC = () => {
  useScreenView('Terminal', 'TerminalBoxCheck');
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data: box, isFetching, isError, error, refetch } = useTransportBoxByCodeQuery(scannedCode);

  const { flash } = useScanScreen({
    onScan: (code) => {
      if (isError && scannedCode === code) { void refetch(); return; }
      setScannedCode(code);
    },
  });

  // Emit exactly one flash when a scan resolves.
  React.useEffect(() => {
    if (!scannedCode || isFetching) return;
    if (isError) flash('err', scannedCode);
    else if (box) flash('ok', box.code ?? scannedCode);
    else flash('err', scannedCode); // not found
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scannedCode, isFetching, isError, box]);

  const subject = box
    ? <SubjectHeader code={box.code} state={box.state} facts={`${box.items?.length ?? 0} položek`} />
    : <SubjectHeader emptyPrompt="Naskenujte box ke kontrole" />;

  return (
    <ScanShell subject={subject}>
      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}
      {isError && error && (
        <TerminalError message={getTerminalErrorMessage(error)} hint="Zkontrolujte kód a naskenujte znovu" />
      )}
      {!isFetching && box && <BoxDetail box={box} />}
    </ScanShell>
  );
};

export default TransportBoxCheck;
