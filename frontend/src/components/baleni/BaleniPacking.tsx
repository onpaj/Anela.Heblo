import { useState, useEffect, type ReactNode } from 'react';
import { ScanLine, Loader2 } from 'lucide-react';
import ScanInput from '../terminal/ScanInput';
import { useScanPackingOrder } from '../../api/hooks/useScanPackingOrder';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';
import PackingOrderMeta from './PackingOrderMeta';
import PackingCoolingIndicator from './PackingCoolingIndicator';
import PackingStateWarning from './PackingStateWarning';
import PackingOrderNotes from './PackingOrderNotes';
import PackingItems from './PackingItems';
import PackingShipmentCreator from './PackingShipmentCreator';
import { useScreenView } from '../../telemetry/useScreenView';
import { usePackingUser } from './packingUser/PackingUserContext';

function CenteredMessage({ children }: { children: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center py-24 text-center text-neutral-gray">
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
}

function OrderBody({ order, shipment, isShowingDoneView, onDoneStateChange }: OrderBodyProps) {
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
      />
      {!isShowingDoneView && (
        <>
          <p className="text-xs uppercase tracking-wide text-neutral-gray">
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

  useEffect(() => {
    if (!current) openPicker();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const handleScan = (value: string) => {
    if (!current) {
      openPicker();
      return;
    }
    scanMutation.mutate({ orderCode: value, packingUserId: current.id });
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
          <p className="text-base font-semibold text-neutral-slate">
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
        />
      );
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
      <div className="flex justify-end">
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
            loading={scanMutation.isPending}
            autoFocusOnMount
            refocusOnBlur
            allowKeyboardToggle
          />
        </div>
      </div>
      {renderBody()}
    </div>
  );
}

export default BaleniPacking;
