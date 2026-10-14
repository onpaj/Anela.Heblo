import React from 'react';
import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp } from 'lucide-react';
import type { LabelSampleState } from './useLabelSample';

interface LabelOffsetPreviewProps {
  sampleState: LabelSampleState;
  carrierName: string;
  /** Moves the label by one step: dx towards the right edge, dy towards the top edge. */
  onNudge: (dxMm: number, dyMm: number) => void;
}

const ARROW_CLASS =
  'flex items-center justify-center w-14 h-14 rounded-xl bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border shadow-soft text-primary-blue dark:text-graphite-accent hover:border-primary-blue active:bg-secondary-blue-pale dark:active:bg-graphite-surface-2 transition-colors';

const PreviewBody: React.FC<{ sampleState: LabelSampleState; carrierName: string }> = ({
  sampleState,
  carrierName,
}) => {
  const { status, previewUrl, sample } = sampleState;

  if (status === 'none') {
    return (
      <p className="p-6 text-sm text-center text-neutral-gray dark:text-graphite-muted">
        Pro dopravce {carrierName} není k dispozici žádný nedávný štítek. Zabalte nejdřív jednu zásilku.
      </p>
    );
  }
  if (status === 'error') {
    return (
      <p className="p-6 text-sm text-center text-red-600 dark:text-red-300">
        Ukázkový štítek se nepodařilo načíst. Zkuste to znovu.
      </p>
    );
  }
  if (!previewUrl) {
    return (
      <div className="flex items-center justify-center h-full" role="status">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-blue" />
      </div>
    );
  }
  return (
    <iframe
      title={`Ukázkový štítek ${carrierName} (objednávka ${sample?.orderCode})`}
      src={`${previewUrl}#toolbar=0&navpanes=0`}
      className="w-full h-full bg-white"
    />
  );
};

/** Sample label with an arrow on each side; every click moves the label 1 mm that way. */
const LabelOffsetPreview: React.FC<LabelOffsetPreviewProps> = ({ sampleState, carrierName, onNudge }) => (
  <div className="grid grid-cols-[auto_1fr_auto] grid-rows-[auto_1fr_auto] gap-3 items-center justify-items-center">
    <div />
    <button type="button" aria-label="Posunout nahoru" className={ARROW_CLASS} onClick={() => onNudge(0, 1)}>
      <ArrowUp className="h-7 w-7" />
    </button>
    <div />

    <button type="button" aria-label="Posunout doleva" className={ARROW_CLASS} onClick={() => onNudge(-1, 0)}>
      <ArrowLeft className="h-7 w-7" />
    </button>
    <div className="w-full max-w-sm aspect-[2/3] border-2 border-dashed border-border-light dark:border-graphite-border rounded-lg overflow-hidden bg-white dark:bg-graphite-surface">
      <PreviewBody sampleState={sampleState} carrierName={carrierName} />
    </div>
    <button type="button" aria-label="Posunout doprava" className={ARROW_CLASS} onClick={() => onNudge(1, 0)}>
      <ArrowRight className="h-7 w-7" />
    </button>

    <div />
    <button type="button" aria-label="Posunout dolů" className={ARROW_CLASS} onClick={() => onNudge(0, -1)}>
      <ArrowDown className="h-7 w-7" />
    </button>
    <div />
  </div>
);

export default LabelOffsetPreview;
