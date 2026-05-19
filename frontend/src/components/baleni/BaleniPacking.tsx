import { useState, type ReactNode } from 'react';
import { ScanLine, Loader2 } from 'lucide-react';
import ScanInput from '../terminal/ScanInput';
import { usePackingOrder, PackingOrderNotFoundError } from '../../api/hooks/usePackingOrder';
import PackingOrderMeta from './PackingOrderMeta';
import PackingItems from './PackingItems';

function CenteredMessage({ children }: { children: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center py-24 text-center text-neutral-gray">
      {children}
    </div>
  );
}

function BaleniPacking() {
  const [scannedCode, setScannedCode] = useState<string | null>(null);
  const { data, isLoading, isError, error, refetch } = usePackingOrder(scannedCode);

  const handleScan = (value: string) => {
    if (value === scannedCode) {
      void refetch();
    } else {
      setScannedCode(value);
    }
  };

  const renderBody = () => {
    if (isLoading) {
      return (
        <CenteredMessage>
          <Loader2 className="h-8 w-8 animate-spin mb-3" />
          <p>Načítám objednávku…</p>
        </CenteredMessage>
      );
    }
    if (isError) {
      const notFound = error instanceof PackingOrderNotFoundError;
      return (
        <CenteredMessage>
          <p className="text-base font-semibold text-neutral-slate">
            {notFound ? 'Objednávka nenalezena' : 'Nepodařilo se načíst objednávku'}
          </p>
          <p className="text-sm mt-1">Naskenujte objednávku znovu.</p>
        </CenteredMessage>
      );
    }
    if (data) {
      return <PackingItems items={data.items} />;
    }
    return (
      <CenteredMessage>
        <ScanLine className="h-10 w-10 mb-3" />
        <p className="text-base font-semibold text-neutral-slate">Naskenujte číslo objednávky</p>
      </CenteredMessage>
    );
  };

  return (
    <div className="flex flex-col gap-4" data-testid="baleni-packing">
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1 min-w-0">
          {data && <PackingOrderMeta order={data} />}
        </div>
        <div className="w-72 shrink-0">
          <ScanInput
            label="Sken čísla objednávky"
            placeholder="Naskenujte objednávku…"
            onScan={handleScan}
            loading={isLoading}
            autoFocusOnMount
            refocusOnBlur
            allowKeyboardToggle
          />
        </div>
      </div>
      {data && (
        <p className="text-xs uppercase tracking-wide text-neutral-gray">
          Položky ({data.items.length})
        </p>
      )}
      {renderBody()}
    </div>
  );
}

export default BaleniPacking;
