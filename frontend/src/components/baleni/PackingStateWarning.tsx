import { AlertTriangle } from 'lucide-react';
import type { PackingOrder } from '../../api/hooks/useScanPackingOrder';

interface PackingStateWarningProps {
  order: PackingOrder;
}

/**
 * Large danger banner shown when a scanned order is NOT eligible for packing.
 * Displays warning text from the backend eligibility response.
 * Purely visual — it does not capture focus, so the scanner input stays ready.
 */
function PackingStateWarning({ order }: PackingStateWarningProps) {
  if (order.eligibility.isEligible) {
    return null;
  }

  return (
    <div
      data-testid="packing-state-warning"
      role="alert"
      className="flex items-center gap-4 rounded-xl border-2 border-red-500 bg-red-50 px-5 py-4"
    >
      <AlertTriangle className="h-12 w-12 shrink-0 text-red-600" strokeWidth={2.5} />
      <div>
        {order.eligibility.warningTitle && (
          <p className="text-xl font-bold text-red-700">{order.eligibility.warningTitle}</p>
        )}
        {order.eligibility.warningBody && (
          <p className="text-sm text-red-600">{order.eligibility.warningBody}</p>
        )}
      </div>
    </div>
  );
}

export default PackingStateWarning;
