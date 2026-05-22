import type { PackingOrder } from '../../api/hooks/useScanPackingOrder';

interface PackingOrderMetaProps {
  order: PackingOrder;
}

function formatAddress(address: PackingOrder['shippingAddress']): string | null {
  if (!address) return null;
  const street = address.street ?? '';
  const cityZip = [address.zip, address.city].filter(Boolean).join(' ');
  return [street, cityZip].filter(Boolean).join(', ') || null;
}

function PackingOrderMeta({ order }: PackingOrderMetaProps) {
  const address = formatAddress(order.shippingAddress);
  return (
    <div data-testid="packing-order-meta">
      <h2 className="text-lg font-bold text-neutral-slate">Objednávka {order.code}</h2>
      <p className="text-sm text-neutral-gray">{order.customerName}</p>
      {address && <p className="text-sm text-neutral-gray">{address}</p>}
      <p className="text-sm text-neutral-gray">
        Doprava: <span className="text-neutral-slate font-medium">{order.shippingMethodName}</span>
      </p>
    </div>
  );
}

export default PackingOrderMeta;
