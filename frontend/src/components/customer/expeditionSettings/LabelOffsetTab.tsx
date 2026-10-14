import { useState, useEffect } from 'react';
import {
  Carriers,
  CarrierLabelOffsetDto,
  MAX_LABEL_OFFSET_MM,
  useCarrierLabelOffsets,
  useSetCarrierLabelOffset,
} from '../../../api/hooks/useCarrierLabelOffsets';

const CARRIER_NAMES: Record<Carriers, string> = {
  [Carriers.Zasilkovna]: 'Zásilkovna',
  [Carriers.PPL]: 'PPL',
  [Carriers.GLS]: 'GLS',
  [Carriers.Osobak]: 'Osobní odběr',
};

const INPUT_CLASS =
  'block w-24 rounded-md border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-indigo-500';

const isInRange = (value: number) =>
  Number.isInteger(value) && Math.abs(value) <= MAX_LABEL_OFFSET_MM;

interface OffsetRowProps {
  offset: CarrierLabelOffsetDto;
}

function OffsetRow({ offset }: OffsetRowProps) {
  const { mutate: saveOffset, isPending, isError } = useSetCarrierLabelOffset();
  const carrier = offset.carrier as Carriers;
  const name = CARRIER_NAMES[carrier] ?? carrier;

  const [offsetXMm, setOffsetXMm] = useState(offset.offsetXMm ?? 0);
  const [offsetYMm, setOffsetYMm] = useState(offset.offsetYMm ?? 0);

  useEffect(() => {
    setOffsetXMm(offset.offsetXMm ?? 0);
    setOffsetYMm(offset.offsetYMm ?? 0);
  }, [offset.offsetXMm, offset.offsetYMm]);

  const isValid = isInRange(offsetXMm) && isInRange(offsetYMm);
  const isDirty = offsetXMm !== (offset.offsetXMm ?? 0) || offsetYMm !== (offset.offsetYMm ?? 0);

  return (
    <tr className="border-b border-gray-100 dark:border-graphite-border">
      <td className="py-3 pr-4 text-sm font-medium text-gray-900 dark:text-graphite-text">{name}</td>
      <td className="py-3 pr-4">
        <input
          type="number"
          step={1}
          min={-MAX_LABEL_OFFSET_MM}
          max={MAX_LABEL_OFFSET_MM}
          aria-label={`${name} X (mm)`}
          value={offsetXMm}
          onChange={(e) => setOffsetXMm(Number(e.target.value))}
          className={INPUT_CLASS}
        />
      </td>
      <td className="py-3 pr-4">
        <input
          type="number"
          step={1}
          min={-MAX_LABEL_OFFSET_MM}
          max={MAX_LABEL_OFFSET_MM}
          aria-label={`${name} Y (mm)`}
          value={offsetYMm}
          onChange={(e) => setOffsetYMm(Number(e.target.value))}
          className={INPUT_CLASS}
        />
      </td>
      <td className="py-3 pr-4">
        <button
          type="button"
          onClick={() => saveOffset({ carrier, offsetXMm, offsetYMm })}
          disabled={isPending || !isValid || !isDirty}
          aria-label={`Uložit ${name}`}
          className="inline-flex items-center px-3 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
        >
          {isPending ? 'Ukládám…' : 'Uložit'}
        </button>
      </td>
      <td className="py-3 text-xs">
        {!isValid && (
          <span className="text-red-600 dark:text-red-300">
            Zadejte celé číslo mezi -{MAX_LABEL_OFFSET_MM} a {MAX_LABEL_OFFSET_MM}.
          </span>
        )}
        {isValid && isError && (
          <span className="text-red-600 dark:text-red-300">Uložení se nezdařilo.</span>
        )}
        {isValid && !isError && offset.modifiedAt && (
          <span className="text-gray-400 dark:text-graphite-faint">
            {offset.modifiedAt.toLocaleString('cs-CZ')}
          </span>
        )}
      </td>
    </tr>
  );
}

function LabelOffsetTab() {
  const { data, isLoading, error } = useCarrierLabelOffsets();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32" role="status">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="mx-4 mt-4 p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg text-red-600 dark:text-red-300 text-sm">
        Nepodařilo se načíst posun štítků. Zkuste obnovit stránku.
      </div>
    );
  }

  return (
    <div className="px-4 py-4 max-w-3xl space-y-4">
      <p className="text-sm text-gray-500 dark:text-graphite-muted">
        Posun přepravního štítku před tiskem v milimetrech. X kladné = doprava, záporné = doleva;
        Y kladné = nahoru, záporné = dolů. Směry platí pro PDF štítku dopravce — pokud tiskárna
        štítek otočí, posune se na papíře v jiném směru. Změna platí od dalšího tisku.
      </p>

      <table className="w-full text-left">
        <thead>
          <tr className="border-b border-gray-200 dark:border-graphite-border text-xs uppercase text-gray-500 dark:text-graphite-muted">
            <th className="py-2 pr-4 font-medium">Dopravce</th>
            <th className="py-2 pr-4 font-medium">X (mm)</th>
            <th className="py-2 pr-4 font-medium">Y (mm)</th>
            <th className="py-2 pr-4" />
            <th className="py-2" />
          </tr>
        </thead>
        <tbody>
          {(data ?? []).map((offset) => (
            <OffsetRow key={offset.carrier} offset={offset} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default LabelOffsetTab;
