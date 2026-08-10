import React, { useState } from 'react';
import { Trash2, Plus } from 'lucide-react';
import {
  useCreateAdjustmentMutation,
  useDeleteAdjustmentMutation,
  OvertimeAdjustmentType,
  type OvertimeAdjustmentDto,
} from '../../api/hooks/useOvertime';
import { formatNumber } from '../../utils/formatters';

export const ADJUSTMENT_TYPE_LABELS: Record<OvertimeAdjustmentType, string> = {
  [OvertimeAdjustmentType.Payout]: 'Proplacení',
  [OvertimeAdjustmentType.PurchaseDeduction]: 'Nákup',
  [OvertimeAdjustmentType.Correction]: 'Korekce',
  [OvertimeAdjustmentType.SportBenefit]: 'Benefit sport',
  [OvertimeAdjustmentType.Other]: 'Jiné',
};

interface StatementAdjustmentsPanelProps {
  personId: string;
  year: number;
  month: number;
  adjustments: OvertimeAdjustmentDto[];
  canWrite: boolean;
  isClosed: boolean;
}

const StatementAdjustmentsPanel: React.FC<StatementAdjustmentsPanelProps> = ({
  personId,
  year,
  month,
  adjustments,
  canWrite,
  isClosed,
}) => {
  const createAdjustment = useCreateAdjustmentMutation();
  const deleteAdjustment = useDeleteAdjustmentMutation();
  const [type, setType] = useState<OvertimeAdjustmentType>(OvertimeAdjustmentType.Correction);
  const [hours, setHours] = useState('');
  const [note, setNote] = useState('');

  const canEdit = canWrite && !isClosed;

  const handleAdd = async () => {
    const hoursValue = parseFloat(hours);
    if (Number.isNaN(hoursValue)) return;
    await createAdjustment.mutateAsync({ personId, year, month, type, hours: hoursValue, note });
    setHours('');
    setNote('');
  };

  return (
    <div className="px-6 py-4 bg-gray-50 dark:bg-graphite-surface-2">
      {adjustments.length > 0 && (
        <table className="min-w-full text-sm mb-3">
          <tbody>
            {adjustments.map((adj) => (
              <tr key={adj.id} className="border-b border-gray-200 dark:border-graphite-border last:border-0">
                <td className="py-1.5 pr-4 text-gray-700 dark:text-graphite-muted">
                  {ADJUSTMENT_TYPE_LABELS[adj.type ?? OvertimeAdjustmentType.Other]}
                </td>
                <td className={`py-1.5 pr-4 font-medium ${(adj.hours ?? 0) >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                  {formatNumber(adj.hours ?? null)}
                </td>
                <td className="py-1.5 pr-4 text-gray-500 dark:text-graphite-muted">{adj.note}</td>
                <td className="py-1.5 text-right">
                  {canEdit && (
                    <button
                      onClick={() => deleteAdjustment.mutate(adj.id as number)}
                      aria-label={`Smazat korekci ${ADJUSTMENT_TYPE_LABELS[adj.type ?? OvertimeAdjustmentType.Other]}`}
                      className="text-red-500 hover:text-red-700"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {adjustments.length === 0 && (
        <p className="text-sm text-gray-500 dark:text-graphite-muted mb-3">Žádné korekce</p>
      )}

      {canEdit && (
        <div className="flex items-center gap-2">
          <select
            value={type}
            onChange={(e) => setType(e.target.value as OvertimeAdjustmentType)}
            aria-label="Typ korekce"
            className="border border-gray-300 dark:border-graphite-border rounded px-2 py-1 text-sm bg-white dark:bg-graphite-surface"
          >
            {Object.values(OvertimeAdjustmentType).map((t) => (
              <option key={t} value={t}>
                {ADJUSTMENT_TYPE_LABELS[t]}
              </option>
            ))}
          </select>
          <input
            type="number"
            step="0.01"
            value={hours}
            onChange={(e) => setHours(e.target.value)}
            placeholder="Hodiny"
            aria-label="Hodiny korekce"
            className="border border-gray-300 dark:border-graphite-border rounded px-2 py-1 text-sm w-24 bg-white dark:bg-graphite-surface"
          />
          <input
            type="text"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Poznámka"
            aria-label="Poznámka ke korekci"
            className="border border-gray-300 dark:border-graphite-border rounded px-2 py-1 text-sm flex-1 bg-white dark:bg-graphite-surface"
          />
          <button
            onClick={handleAdd}
            disabled={createAdjustment.isPending || hours === ''}
            className="inline-flex items-center px-3 py-1.5 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors disabled:opacity-50"
          >
            <Plus className="h-4 w-4 mr-1" />
            Přidat
          </button>
        </div>
      )}
    </div>
  );
};

export default StatementAdjustmentsPanel;
