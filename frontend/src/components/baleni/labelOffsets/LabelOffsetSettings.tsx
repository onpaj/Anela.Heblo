import React, { useState } from 'react';
import { Printer, RotateCcw, Save } from 'lucide-react';
import { useScreenView } from '../../../telemetry/useScreenView';
import { fetchAndPrintLabel } from '../printLabelPdf';
import LabelOffsetPreview from './LabelOffsetPreview';
import { useLabelSample } from './useLabelSample';
import {
  CARRIER_NAMES,
  LABEL_CARRIERS,
  LabelCarrier,
  LabelOffset,
  ZERO_OFFSET,
  clampMm,
  loadLabelOffsets,
  saveLabelOffset,
} from './labelOffsetStorage';

type Feedback = { tone: 'ok' | 'error'; text: string } | null;

const savedOffsetOf = (carrier: LabelCarrier): LabelOffset => loadLabelOffsets()[carrier] ?? ZERO_OFFSET;

const formatMm = (mm: number): string => `${mm > 0 ? '+' : ''}${mm} mm`;

const BUTTON_BASE =
  'inline-flex items-center justify-center gap-2 px-4 py-3 rounded-lg text-sm font-medium transition-colors disabled:opacity-50';
const SECONDARY_BUTTON = `${BUTTON_BASE} bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border text-neutral-slate dark:text-graphite-text hover:border-primary-blue`;
const PRIMARY_BUTTON = `${BUTTON_BASE} bg-primary-blue text-white hover:bg-primary-blue/90`;

/**
 * Per-computer label adjustment for the packing desk: pick a carrier, nudge its last label with
 * the arrows, test-print, then save. The offset is stored in this browser only and applied to
 * every label of that carrier printed from this computer.
 */
const LabelOffsetSettings: React.FC = () => {
  useScreenView('Baleni', 'LabelOffsetSettings');

  const [carrier, setCarrier] = useState<LabelCarrier>('Zasilkovna');
  const [savedOffset, setSavedOffset] = useState<LabelOffset>(() => savedOffsetOf('Zasilkovna'));
  const [draft, setDraft] = useState<LabelOffset>(savedOffset);
  const [feedback, setFeedback] = useState<Feedback>(null);
  const [isPrinting, setIsPrinting] = useState(false);

  const sampleState = useLabelSample(carrier, draft);
  const isDirty = draft.xMm !== savedOffset.xMm || draft.yMm !== savedOffset.yMm;

  const selectCarrier = (next: LabelCarrier) => {
    const saved = savedOffsetOf(next);
    setCarrier(next);
    setSavedOffset(saved);
    setDraft(saved);
    setFeedback(null);
  };

  const nudge = (dxMm: number, dyMm: number) => {
    setDraft((prev) => ({ xMm: clampMm(prev.xMm + dxMm), yMm: clampMm(prev.yMm + dyMm) }));
    setFeedback(null);
  };

  const testPrint = async () => {
    if (!sampleState.sample) return;
    setIsPrinting(true);
    const { orderCode, packageNumber } = sampleState.sample;
    const result = await fetchAndPrintLabel(orderCode, packageNumber, undefined, false, { [carrier]: draft });
    setIsPrinting(false);
    setFeedback(
      result.printed
        ? { tone: 'ok', text: 'Zkušební štítek odeslán na tisk.' }
        : { tone: 'error', text: 'Zkušební tisk se nezdařil.' },
    );
  };

  const save = () => {
    if (!saveLabelOffset(carrier, draft)) {
      setFeedback({ tone: 'error', text: 'Prohlížeč nastavení neuložil (úložiště je zablokované).' });
      return;
    }
    setSavedOffset(draft);
    setFeedback({ tone: 'ok', text: `Uloženo. Štítky ${CARRIER_NAMES[carrier]} se z tohoto počítače tisknou s tímto posunem.` });
  };

  return (
    <div className="pt-4 space-y-6">
      <div>
        <h1 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">Nastavení štítků</h1>
        <p className="text-sm text-neutral-gray dark:text-graphite-muted mt-1">
          Posun štítku pro tiskárnu na tomto počítači. Šipkami posuňte ukázkový štítek, vyzkoušejte tisk a uložte.
          Nastavení platí jen v tomto prohlížeči.
        </p>
      </div>

      <div className="flex gap-2" role="group" aria-label="Dopravce">
        {LABEL_CARRIERS.map((c) => (
          <button
            key={c}
            type="button"
            aria-pressed={c === carrier}
            onClick={() => selectCarrier(c)}
            className={`px-5 py-3 rounded-lg text-sm font-medium border transition-colors ${
              c === carrier
                ? 'bg-primary-blue text-white border-primary-blue'
                : 'bg-white dark:bg-graphite-surface text-neutral-slate dark:text-graphite-text border-border-light dark:border-graphite-border hover:border-primary-blue'
            }`}
          >
            {CARRIER_NAMES[c]}
          </button>
        ))}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-[1fr_auto] gap-6 items-start">
        <LabelOffsetPreview sampleState={sampleState} carrierName={CARRIER_NAMES[carrier]} onNudge={nudge} />

        <div className="space-y-4 lg:w-72">
          <dl className="grid grid-cols-2 gap-3">
            <div className="bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-4">
              <dt className="text-xs text-neutral-gray dark:text-graphite-muted">Vodorovně (X)</dt>
              <dd className="text-2xl font-bold text-primary-blue dark:text-graphite-accent" data-testid="offset-x">
                {formatMm(draft.xMm)}
              </dd>
            </div>
            <div className="bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-4">
              <dt className="text-xs text-neutral-gray dark:text-graphite-muted">Svisle (Y)</dt>
              <dd className="text-2xl font-bold text-primary-blue dark:text-graphite-accent" data-testid="offset-y">
                {formatMm(draft.yMm)}
              </dd>
            </div>
          </dl>
          <p className="text-xs text-neutral-gray dark:text-graphite-muted">
            Uloženo: X {formatMm(savedOffset.xMm)}, Y {formatMm(savedOffset.yMm)}
            {isDirty && <span className="ml-1 font-medium text-amber-600">(neuložené změny)</span>}
          </p>

          <div className="grid grid-cols-1 gap-2">
            <button type="button" className={SECONDARY_BUTTON} onClick={() => setDraft(ZERO_OFFSET)}>
              <RotateCcw className="h-4 w-4" /> Vynulovat
            </button>
            <button
              type="button"
              className={SECONDARY_BUTTON}
              onClick={testPrint}
              disabled={!sampleState.sample || isPrinting}
            >
              <Printer className="h-4 w-4" /> {isPrinting ? 'Tisknu…' : 'Zkušební tisk'}
            </button>
            <button type="button" className={PRIMARY_BUTTON} onClick={save} disabled={!isDirty}>
              <Save className="h-4 w-4" /> Uložit
            </button>
          </div>

          {feedback && (
            <p
              role={feedback.tone === 'error' ? 'alert' : 'status'}
              className={`text-sm ${feedback.tone === 'error' ? 'text-red-600 dark:text-red-300' : 'text-green-700 dark:text-green-300'}`}
            >
              {feedback.text}
            </p>
          )}
        </div>
      </div>
    </div>
  );
};

export default LabelOffsetSettings;
