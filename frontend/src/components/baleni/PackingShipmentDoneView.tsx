import { CheckCircle2 } from 'lucide-react';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';

interface PackingShipmentDoneViewProps {
  order: PackingOrder;
  shipment: ScanShipment;
  resolvedTrackingNumbers?: string[] | null;
  onReprint: () => void;
}

interface AddressLines {
  street: string;
  cityZip: string;
}

function buildAddressLines(order: PackingOrder): AddressLines | null {
  const address = order.shippingAddress;
  if (!address) return null;
  const street = address.street ?? '';
  const cityZip = [address.zip, address.city].filter(Boolean).join(' ');
  if (!street && !cityZip) return null;
  return { street, cityZip };
}

function PackingShipmentDoneView({
  order,
  shipment,
  resolvedTrackingNumbers,
  onReprint,
}: PackingShipmentDoneViewProps) {
  const addressLines = buildAddressLines(order);
  const packageTrackingNumbers = shipment.packages
    .map((p) => p.trackingNumber)
    .filter((trackingNumber): trackingNumber is string => Boolean(trackingNumber));
  const trackingNumbers =
    resolvedTrackingNumbers && resolvedTrackingNumbers.length > 0
      ? resolvedTrackingNumbers
      : packageTrackingNumbers;
  const trackingSummary = trackingNumbers.length > 0 ? trackingNumbers.join(', ') : '—';

  return (
    <div
      data-testid="packing-shipment-done"
      className="rounded-2xl bg-white dark:bg-graphite-surface p-8 shadow-lg dark:shadow-soft-dark flex flex-col gap-6"
    >
      <div className="flex items-center gap-4">
        <CheckCircle2 className="h-10 w-10 text-success" />
        <h2 className="text-3xl font-bold text-neutral-slate dark:text-graphite-text">Zakázka byla vyexpedována</h2>
      </div>

      <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray dark:text-graphite-muted">Číslo objednávky</dt>
          <dd className="text-base text-neutral-slate dark:text-graphite-text font-medium">{order.code}</dd>
        </div>

        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray dark:text-graphite-muted">Zákazník</dt>
          <dd className="text-base text-neutral-slate dark:text-graphite-text font-medium">{order.customerName}</dd>
        </div>

        {addressLines && (
          <div className="flex flex-col gap-1" data-testid="packing-shipment-done-address">
            <dt className="text-sm text-neutral-gray dark:text-graphite-muted">Adresa</dt>
            <dd className="text-base text-neutral-slate dark:text-graphite-text font-medium">
              {addressLines.street && <div>{addressLines.street}</div>}
              {addressLines.cityZip && <div>{addressLines.cityZip}</div>}
            </dd>
          </div>
        )}

        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray dark:text-graphite-muted">Doprava</dt>
          <dd className="text-base text-neutral-slate dark:text-graphite-text font-medium">{order.shippingMethodName}</dd>
        </div>

        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray dark:text-graphite-muted">Číslo zásilky</dt>
          <dd className="text-base text-neutral-slate dark:text-graphite-text font-medium">{trackingSummary}</dd>
        </div>
      </dl>

      <button
        type="button"
        data-testid="reprint-label-button"
        className="w-full rounded-xl bg-primary-blue py-5 text-lg font-semibold text-white shadow active:scale-95"
        onClick={onReprint}
      >
        Vytisknout štítek znovu
      </button>
    </div>
  );
}

export default PackingShipmentDoneView;
