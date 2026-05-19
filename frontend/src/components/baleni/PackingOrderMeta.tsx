import { Snowflake } from 'lucide-react';
import type { PackingOrder } from '../../api/hooks/usePackingOrder';

interface PackingOrderMetaProps {
  order: PackingOrder;
}

function CoolingBadge({ order }: PackingOrderMetaProps) {
  if (!order.isCooled) {
    return (
      <span className="inline-flex items-center rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-semibold text-neutral-gray">
        Bez chlazení
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-secondary-blue-pale px-2.5 py-0.5 text-xs font-bold text-primary-blue">
      <Snowflake className="h-3 w-3" />
      Chlazení {order.cooling}
    </span>
  );
}

function PackingOrderMeta({ order }: PackingOrderMetaProps) {
  return (
    <div data-testid="packing-order-meta">
      <h2 className="text-lg font-bold text-neutral-slate">Objednávka {order.code}</h2>
      <p className="text-sm text-neutral-gray">{order.customerName}</p>
      <p className="text-sm text-neutral-gray">
        Doprava: <span className="text-neutral-slate font-medium">{order.shippingMethodName}</span>
      </p>
      <div className="mt-1.5">
        <CoolingBadge order={order} />
      </div>
    </div>
  );
}

export default PackingOrderMeta;
