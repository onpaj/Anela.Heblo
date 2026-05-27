import React, { useState } from 'react';
import { Loader2, AlertCircle } from 'lucide-react';
import ScanInput from './ScanInput';
import { useTransportBoxByCodeQuery } from '../../api/hooks/useTransportBoxes';
import TerminalError from './TerminalError';
import { getTerminalErrorMessage } from './terminalErrors';
import BoxDetail from './TransportBoxDetail';
import { useScreenView } from '../../telemetry/useScreenView';

const TransportBoxCheck: React.FC = () => {
  useScreenView('Terminal', 'TerminalBoxCheck');
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data: box, isFetching, isError, error, refetch } = useTransportBoxByCodeQuery(scannedCode);

  const showError = !!scannedCode && !isFetching && isError;

  const handleScan = (value: string) => {
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
          suppressKeyboard
          allowKeyboardToggle
        />
      </div>

      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
        </div>
      )}

      {isError && error && (
        <TerminalError
          message={getTerminalErrorMessage(error)}
          hint="Zkontrolujte kód a naskenujte znovu"
        />
      )}

      {showError && !error && (
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

      {!isFetching && box && <BoxDetail box={box} />}
    </div>
  );
};

export default TransportBoxCheck;
