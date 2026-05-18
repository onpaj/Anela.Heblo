import { useState } from 'react';
import {
  Carriers,
  CarrierGroupDto,
  Cooling,
  DeliveryHandling,
  SetCarrierCoolingRequest,
} from '../../../api/hooks/useCarrierCooling';

interface CarrierCoolingMatrixProps {
  groups: CarrierGroupDto[];
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
}

const CARRIER_LABELS: Record<Carriers, string> = {
  Zasilkovna: 'Zásilkovna',
  PPL: 'PPL',
  GLS: 'GLS',
  Osobak: 'Osobní odběr',
};

const HANDLING_LABELS: Record<DeliveryHandling, string> = {
  NaRuky: 'Na ruky',
  Box: 'Box',
};

const COOLING_OPTIONS: { value: Cooling; label: string }[] = [
  { value: 'None', label: 'Bez chlazení' },
  { value: 'L1', label: 'L1' },
  { value: 'L2', label: 'L2' },
];

function CarrierCoolingMatrix({ groups, onSetCooling, isSaving }: CarrierCoolingMatrixProps) {
  const [savingKey, setSavingKey] = useState<string | null>(null);

  const handleChange = (
    carrier: Carriers,
    deliveryHandling: DeliveryHandling,
    cooling: Cooling
  ) => {
    const key = `${carrier}-${deliveryHandling}`;
    setSavingKey(key);
    onSetCooling({ carrier, deliveryHandling, cooling });
    setTimeout(() => setSavingKey(null), 1500);
  };

  return (
    <div className="space-y-4 p-4">
      {groups.map((group) => (
        <div
          key={group.carrier}
          className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden"
        >
          <div className="px-4 py-3 border-b border-gray-100 bg-gray-50">
            <h2 className="text-sm font-semibold text-gray-800">
              {CARRIER_LABELS[group.carrier] ?? `Dopravce ${group.carrier}`}
            </h2>
          </div>
          <div className="divide-y divide-gray-50">
            {group.rows.map((row) => {
              const key = `${group.carrier}-${row.deliveryHandling}`;
              const isSavingRow = savingKey === key;

              return (
                <div
                  key={row.deliveryHandling}
                  className="flex items-center px-4 py-3 gap-6"
                >
                  <span className="w-24 text-sm text-gray-700 flex-shrink-0">
                    {HANDLING_LABELS[row.deliveryHandling] ?? String(row.deliveryHandling)}
                  </span>
                  <div className="flex gap-6">
                    {COOLING_OPTIONS.map((option) => (
                      <label
                        key={option.value}
                        className="flex items-center gap-2 cursor-pointer"
                      >
                        <input
                          type="radio"
                          name={key}
                          value={option.value}
                          checked={row.cooling === option.value}
                          onChange={() =>
                            handleChange(group.carrier, row.deliveryHandling, option.value)
                          }
                          disabled={isSaving}
                          className="h-4 w-4 text-indigo-600 cursor-pointer"
                        />
                        <span className="text-sm text-gray-700">{option.label}</span>
                      </label>
                    ))}
                  </div>
                  {isSavingRow && (
                    <span className="text-xs text-gray-400 ml-2 animate-pulse">
                      Ukládám…
                    </span>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

export default CarrierCoolingMatrix;
