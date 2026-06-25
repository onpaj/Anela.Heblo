import { AlertTriangle } from 'lucide-react';
import type { PackingOrder } from '../../api/hooks/useScanPackingOrder';

interface PackingStateWarningProps {
  order: PackingOrder;
}

function PackingStateWarning({ order }: PackingStateWarningProps) {
  if (order.eligibility.isEligible) {
    return null;
  }

  return (
    <div
      data-testid="packing-state-warning"
      role="alert"
      className="flex items-center gap-4 rounded-xl border-2 border-red-500 dark:border-red-500/60 bg-red-50 dark:bg-red-900/20 px-5 py-4"
    >
      <AlertTriangle className="h-12 w-12 shrink-0 text-red-600 dark:text-red-400" strokeWidth={2.5} />
      <div>
        <p className="text-xl font-bold text-red-700 dark:text-red-300">Objednávka není ve stavu „Balí se"</p>
        <p className="text-sm text-red-600 dark:text-red-400">Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.</p>
      </div>
    </div>
  );
}

export default PackingStateWarning;
