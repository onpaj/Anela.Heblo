import { useState, useEffect, type ReactNode } from 'react';
import { ScanLine, Loader2, PackagePlus } from 'lucide-react';
import ScanInput from '../terminal/ScanInput';
import { useScanPackingOrder } from '../../api/hooks/useScanPackingOrder';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';
import PackingOrderMeta from './PackingOrderMeta';
import PackingCoolingIndicator from './PackingCoolingIndicator';
import PackingStateWarning from './PackingStateWarning';
import PackingOrderNotes from './PackingOrderNotes';
import PackingItems from './PackingItems';
import PackingShipmentCreator from './PackingShipmentCreator';
import MultiPackageModal from './MultiPackageModal';
import { useScreenView } from '../../telemetry/useScreenView';
import { usePackingUser } from './packingUser/PackingUserContext';

function CenteredMessage({ children }: { children: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center py-24 text-center text-neutral-gray dark:text-graphite-muted">
      {children}
    </div>
  );
}

const ORDER_NOT_FOUND_MESSAGE = 'Objednávka nebyla nalezena.';

function isOrderNotFoundError(error: Error): boolean {
  return error.message === ORDER_NOT_FOUND_MESSAGE;
}

interface OrderBodyProps {
  order: PackingOrder;
  shipment: ScanShipment | null;
  isShowingDoneView: boolean;
  onDoneStateChange: (isDone: boolean) => void;
  onPrintModalOpenChange: (isOpen: boolean) => void;
}

function OrderBody({ order, shipment, isShowingDoneView, onDoneStateChange, onPrintModalOpenChange }: OrderBodyProps) {
  return (
    <>
      <PackingStateWarning order={order} />
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <PackingOrderMeta order={order} />
        </div>
        <div className="flex flex-1 justify-center">
          <PackingCoolingIndicator order={order} />
        </div>
      </div>
      <PackingOrderNotes customerNote={order.customerNote} eshopNote={order.eshopNote} />
      <PackingShipmentCreator
        order={order}
        scanShipment={shipment}
        onDoneStateChange={onDoneStateChange}
        onPrintModalOpenChange={onPrintModalOpenChange}
      />
      {!isShowingDoneView && (
        <>
          <p className="text-xs uppercase tracking-wide text-neutral-gray dark:text-graphite-muted">
            Položky ({order.items.length})
          </p>
          <PackingItems items={order.items} />
        </>
      )}
    </>
  );
}

function BaleniPacking() {
  useScreenView('Baleni', 'BaleniPacking');
  const { current, openPicker } = usePackingUser();
  const scanMutation = useScanPackingOrder();
  const [isShowingDoneView, setIsShowingDoneView] = useState(false);
  const [isMultiModalOpen, setIsMultiModalOpen] = useState(false);
  const [isPrintModalOpen, setIsPrintModalOpen] = useState(false);

  useEffect(() => {
    if (!current) openPicker();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const handleScan = (value: string, numberOfPackages = 1) => {
    if (!current) {
      openPicker();
      return;
    }
    scanMutation.mutate({ orderCode: value, numberOfPackages, packingUserId: current.id });
  };

  const handleMultiConfirm = (orderCode: string, numberOfPackages: number) => {
    setIsMultiModalOpen(false);
    handleScan(orderCode, numberOfPackages);
  };

  const renderBody = () => {
    if (scanMutation.isPending) {
      return (
        <CenteredMessage>
          <Loader2 className="h-8 w-8 animate-spin mb-3" />
          <p>Načítám objednávku…</p>
        </CenteredMessage>
      );
    }

    if (scanMutation.isError && scanMutation.error) {
      const notFound = isOrderNotFoundError(scanMutation.error);
      return (
        <CenteredMessage>
          <p className="text-base font-semibold text-neutral-slate dark:text-graphite-text">
            {notFound ? 'Objednávka nenalezena' : 'Nepodařilo se načíst objednávku'}
          </p>
          <p className="text-sm mt-1">Naskenujte objednávku znovu.</p>
        </CenteredMessage>
      );
    }

    if (scanMutation.data) {
      return (
        <OrderBody
          order={scanMutation.data.order}
          shipment={scanMutation.data.shipment}
          isShowingDoneView={isShowingDoneView}
          onDoneStateChange={setIsShowingDoneView}
          onPrintModalOpenChange={setIsPrintModalOpen}
        />
      );
    }

    return (
      <CenteredMessage>
        <ScanLine className="h-10 w-10 mb-3" />
        <p className="text-base font-semibold text-neutral-slate dark:text-graphite-text">Naskenujte číslo objednávky</p>
      </CenteredMessage>
    );
  };

  return (
    <div className="flex flex-col gap-4" data-testid="baleni-packing">
      <div className="flex items-end justify-end gap-2">
        <button
          type="button"
          data-testid="multi-package-button"
          aria-label="Více balíků"
          onClick={() => setIsMultiModalOpen(true)}
          className="flex h-14 items-center gap-2 rounded-xl border-2 border-neutral-300 dark:border-graphite-border bg-white dark:bg-graphite-surface px-4 text-base font-semibold text-neutral-slate dark:text-graphite-text shadow dark:shadow-soft-dark active:scale-95"
        >
          <PackagePlus className="h-5 w-5" />
          Více balíků
        </button>
        <div className="w-72 shrink-0">
          {!current && (
            <p className="text-sm text-center text-amber-600">
              Nejprve vyberte baliče
            </p>
          )}
          <ScanInput
            label="Sken čísla objednávky"
            placeholder="Naskenujte objednávku…"
            onScan={handleScan}
            loading={scanMutation.isPending || !current}
            autoFocusOnMount
            refocusOnBlur={!isMultiModalOpen && !isPrintModalOpen}
            allowKeyboardToggle
          />
        </div>
      </div>
      {isMultiModalOpen && (
        <MultiPackageModal
          onConfirm={handleMultiConfirm}
          onClose={() => setIsMultiModalOpen(false)}
        />
      )}
      {renderBody()}
    </div>
  );
}

export default BaleniPacking;
