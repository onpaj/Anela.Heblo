import { useState, useRef, useEffect } from 'react';
import {
  Carriers,
  CarrierCoolingRowDto,
  CarrierGroupDto,
  Cooling,
  DeliveryHandling,
  SetCarrierCoolingRequest,
} from '../../../api/hooks/useCarrierCooling';

interface CarrierCoolingMatrixProps {
  groups: CarrierGroupDto[];
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
  savingRow: { carrier: Carriers; deliveryHandling: DeliveryHandling } | null;
}

const CARRIER_LABELS: Record<Carriers, string> = {
  Zasilkovna: 'Zásilkovna',
  PPL: 'PPL',
  GLS: 'GLS',
  Osobak: 'Osobní odběr',
};

const HANDLING_LABELS: Record<DeliveryHandling, string> = {
  NaRuky: 'Do ruky',
  Box: 'Box',
};

const COOLING_OPTIONS: { value: Cooling; label: string }[] = [
  { value: 'None', label: 'Bez chlazení' },
  { value: 'L1', label: 'L1' },
  { value: 'L2', label: 'L2' },
];

const DEFAULT_COOLING_TEXT = 'CHLAZENÁ ZÁSILKA';
const COOLING_TEXT_MAX_LENGTH = 50;

interface CarrierCoolingRowProps {
  carrier: Carriers;
  row: CarrierCoolingRowDto;
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
  isThisRowSaving: boolean;
}

function CarrierCoolingRow({
  carrier,
  row,
  onSetCooling,
  isSaving,
  isThisRowSaving,
}: CarrierCoolingRowProps) {
  const [text, setText] = useState<string>(row.coolingText ?? '');
  const rowRef = useRef(row);
  useEffect(() => { rowRef.current = row; }, [row]);
  const radioName = `${carrier}-${row.deliveryHandling}`;

  const commitText = () => {
    const normalized = text.trim();
    const current = rowRef.current.coolingText ?? '';
    if (normalized === current) return;
    onSetCooling({
      carrier,
      deliveryHandling: rowRef.current.deliveryHandling,
      cooling: rowRef.current.cooling,
      coolingText: normalized === '' ? null : normalized,
    });
  };

  return (
    <div className="flex items-center px-3 py-2 gap-4">
      <span className="w-20 text-sm text-gray-700 flex-shrink-0">
        {HANDLING_LABELS[row.deliveryHandling] ?? String(row.deliveryHandling)}
      </span>
      <div className="flex gap-4">
        {COOLING_OPTIONS.map((option) => (
          <label key={option.value} className="flex items-center gap-2 cursor-pointer">
            <input
              type="radio"
              name={radioName}
              value={option.value}
              checked={row.cooling === option.value}
              onChange={() =>
                onSetCooling({
                  carrier,
                  deliveryHandling: row.deliveryHandling,
                  cooling: option.value,
                  coolingText: row.coolingText ?? null,
                })
              }
              disabled={isSaving}
              className="h-4 w-4 text-indigo-600 cursor-pointer"
            />
            <span className="text-sm text-gray-700">{option.label}</span>
          </label>
        ))}
      </div>
      <input
        type="text"
        value={text}
        maxLength={COOLING_TEXT_MAX_LENGTH}
        placeholder={DEFAULT_COOLING_TEXT}
        onChange={(e) => setText(e.target.value)}
        onBlur={commitText}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            (e.target as HTMLInputElement).blur();
          }
        }}
        disabled={isSaving}
        className="flex-1 min-w-0 text-sm border border-gray-300 rounded px-2 py-1 focus:outline-none focus:ring-1 focus:ring-indigo-500"
      />
      {isThisRowSaving && (
        <span className="text-xs text-gray-400 ml-2 animate-pulse">Ukládám…</span>
      )}
    </div>
  );
}

function CarrierCoolingMatrix({ groups, onSetCooling, isSaving, savingRow }: CarrierCoolingMatrixProps) {
  return (
    <div className="space-y-3 p-4">
      {groups.map((group) => (
        <div
          key={group.carrier}
          className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden"
        >
          <div className="px-3 py-2 border-b border-gray-100 bg-gray-50">
            <h2 className="text-sm font-semibold text-gray-800">
              {CARRIER_LABELS[group.carrier] ?? `Dopravce ${group.carrier}`}
            </h2>
          </div>
          <div className="divide-y divide-gray-50">
            {group.rows.map((row) => {
              const isThisRowSaving =
                isSaving &&
                savingRow?.carrier === group.carrier &&
                savingRow?.deliveryHandling === row.deliveryHandling;

              return (
                <CarrierCoolingRow
                  key={row.deliveryHandling}
                  carrier={group.carrier}
                  row={row}
                  onSetCooling={onSetCooling}
                  isSaving={isSaving}
                  isThisRowSaving={isThisRowSaving}
                />
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

export default CarrierCoolingMatrix;
