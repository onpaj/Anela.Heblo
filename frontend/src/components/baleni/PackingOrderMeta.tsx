import type { PackingOrder } from '../../api/hooks/useScanPackingOrder';

interface PackingOrderMetaProps {
  order: PackingOrder;
}

function PackingOrderMeta({ order }: PackingOrderMetaProps) {
  return (
    <div data-testid="packing-order-meta">
      <h2 className="text-lg font-bold text-neutral-slate">Objednávka {order.code}</h2>
      <p className="text-sm text-neutral-gray">{order.customerName}</p>
      <p className="text-sm text-neutral-gray">
        Doprava: <span className="text-neutral-slate font-medium">{order.shippingMethodName}</span>
      </p>
    </div>
  );
}

export default PackingOrderMeta;
