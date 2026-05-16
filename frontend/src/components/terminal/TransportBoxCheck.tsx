import React, { useState } from 'react';
import { Loader2, PackageX, AlertCircle } from 'lucide-react';
import ScanInput from './ScanInput';
import { useTransportBoxByCodeQuery } from '../../api/hooks/useTransportBoxes';
import BoxDetail from './TransportBoxDetail';

const TransportBoxCheck: React.FC = () => {
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data: box, isFetching, isError } = useTransportBoxByCodeQuery(scannedCode);

  const showNotFound = !!scannedCode && !isFetching && !isError && !box;
  const showError = !!scannedCode && !isFetching && isError;

  return (
    <div className="space-y-4">
      <div className="sticky top-0 z-10 bg-background-gray pb-3">
        <ScanInput
          label="Kód boxu"
          onScan={setScannedCode}
          suppressKeyboard
          allowKeyboardToggle
        />
      </div>

      {isFetching && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-8 w-8 animate-spin text-primary-blue" />
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

      {!isFetching && box && <BoxDetail box={box} />}
    </div>
  );
};

export default TransportBoxCheck;
