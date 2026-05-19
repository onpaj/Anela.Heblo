import { AlertTriangle } from 'lucide-react';
import type { PackingOrder } from '../../api/hooks/usePackingOrder';

interface PackingStateWarningProps {
  order: PackingOrder;
}

/**
 * Large danger banner shown when a scanned order is NOT in the "Balí se" packing state.
 * Purely visual — it does not capture focus, so the scanner input stays ready.
 */
function PackingStateWarning({ order }: PackingStateWarningProps) {
  if (order.isInPackingState) {
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
        <p className="text-xl font-bold text-red-700">Objednávka není ve stavu „Balí se"</p>
        <p className="text-sm text-red-600">
          Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.
        </p>
      </div>
    </div>
  );
}

export default PackingStateWarning;
