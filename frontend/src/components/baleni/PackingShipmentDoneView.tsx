import { CheckCircle2 } from 'lucide-react';
import type { PackingOrder, ScanShipment } from '../../api/hooks/useScanPackingOrder';

interface PackingShipmentDoneViewProps {
  order: PackingOrder;
  shipment: ScanShipment;
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

function PackingShipmentDoneView({ order, shipment, onReprint }: PackingShipmentDoneViewProps) {
  const addressLines = buildAddressLines(order);
  const trackingSummary = shipment.packages.map((p) => p.trackingNumber ?? p.name).join(', ');

  return (
    <div
      data-testid="packing-shipment-done"
      className="rounded-2xl bg-white p-8 shadow-lg flex flex-col gap-6"
    >
      <div className="flex items-center gap-4">
        <CheckCircle2 className="h-10 w-10 text-success" />
        <h2 className="text-3xl font-bold text-neutral-slate">Zakázka byla vyexpedována</h2>
      </div>

      <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray">Číslo objednávky</dt>
          <dd className="text-base text-neutral-slate font-medium">{order.code}</dd>
        </div>

        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray">Zákazník</dt>
          <dd className="text-base text-neutral-slate font-medium">{order.customerName}</dd>
        </div>

        {addressLines && (
          <div className="flex flex-col gap-1" data-testid="packing-shipment-done-address">
            <dt className="text-sm text-neutral-gray">Adresa</dt>
            <dd className="text-base text-neutral-slate font-medium">
              {addressLines.street && <div>{addressLines.street}</div>}
              {addressLines.cityZip && <div>{addressLines.cityZip}</div>}
            </dd>
          </div>
        )}

        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray">Doprava</dt>
          <dd className="text-base text-neutral-slate font-medium">{order.shippingMethodName}</dd>
        </div>

        <div className="flex flex-col gap-1">
          <dt className="text-sm text-neutral-gray">Číslo zásilky</dt>
          <dd className="text-base text-neutral-slate font-medium">{trackingSummary}</dd>
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
