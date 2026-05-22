import { Snowflake } from 'lucide-react';
import type { PackingOrder } from '../../api/hooks/useScanPackingOrder';

interface PackingCoolingIndicatorProps {
  order: PackingOrder;
}

/**
 * Prominent cooling indicator. When the order needs a cooling pack it shows a
 * large snowflake so the packer cannot miss it; otherwise a small muted label.
 */
function PackingCoolingIndicator({ order }: PackingCoolingIndicatorProps) {
  if (!order.isCooled) {
    return (
      <span
        data-testid="packing-cooling-indicator"
        className="inline-flex items-center rounded-full bg-gray-100 px-3 py-1 text-sm font-semibold text-neutral-gray"
      >
        Bez chlazení
      </span>
    );
  }

  return (
    <div
      data-testid="packing-cooling-indicator"
      className="flex flex-col items-center text-primary-blue"
    >
      <Snowflake className="h-20 w-20" strokeWidth={2.5} />
      <span className="text-2xl font-bold leading-tight">Chlazení {order.cooling}</span>
    </div>
  );
}

export default PackingCoolingIndicator;
