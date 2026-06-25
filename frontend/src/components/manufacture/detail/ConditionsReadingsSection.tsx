import React from 'react';
import {
  ManufactureOrderConditionsReadingDto,
  ManufactureOrderState,
} from '../../../api/generated/api-client';

const STAGE_LABELS: Partial<Record<ManufactureOrderState, string>> = {
  [ManufactureOrderState.SemiProductManufactured]: 'Polotovar',
  [ManufactureOrderState.Completed]: 'Dokončeno',
};

const ALL_STAGES = [
  ManufactureOrderState.SemiProductManufactured,
  ManufactureOrderState.Completed,
];

const CONDITIONS_SOURCE_LIVE = 1;
const CONDITIONS_SOURCE_PARTIAL = 2;
const CONDITIONS_SOURCE_UNAVAILABLE = 3;

const SourceBadge: React.FC<{ source: number }> = ({ source }) => {
  if (source === CONDITIONS_SOURCE_UNAVAILABLE) {
    return (
      <span className="ml-1 rounded bg-red-100 px-1.5 py-0.5 text-xs font-medium text-red-700 dark:bg-red-900/30 dark:text-red-300">
        HA nedostupný
      </span>
    );
  }
  if (source === CONDITIONS_SOURCE_PARTIAL) {
    return (
      <span className="ml-1 rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-900/30 dark:text-amber-300">
        Částečné
      </span>
    );
  }
  return null;
};

const ValueCell: React.FC<{ value: number | null | undefined }> = ({ value }) =>
  value == null ? <span>—</span> : <span>{value.toFixed(1)}</span>;

interface Props {
  readings: ManufactureOrderConditionsReadingDto[];
}

const ConditionsReadingsSection: React.FC<Props> = ({ readings }) => {
  const byStage = new Map(readings.map((r) => [r.stage, r]));

  return (
    <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-3">
      <h3 className="text-base font-semibold text-gray-800 dark:text-graphite-muted mb-3">
        Podmínky výroby
      </h3>
      <table className="w-full text-sm border-collapse">
        <thead>
          <tr className="border-b dark:border-graphite-border text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase">
            <th className="pb-1 pr-3">Fáze</th>
            <th className="pb-1 pr-3">T vnitřní (°C)</th>
            <th className="pb-1 pr-3">RH vnitřní (%)</th>
            <th className="pb-1 pr-3">T venkovní (°C)</th>
            <th className="pb-1 pr-3">RH venkovní (%)</th>
            <th className="pb-1">Zaznamenáno</th>
          </tr>
        </thead>
        <tbody>
          {ALL_STAGES.map((stage) => {
            const reading = byStage.get(stage);
            return (
              <tr key={stage} className="border-b last:border-0 dark:border-graphite-border">
                <td className="py-1.5 pr-3 font-medium text-gray-700 dark:text-graphite-muted">
                  {STAGE_LABELS[stage]}
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.innerTemperature} />
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.innerHumidity} />
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.outerTemperature} />
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.outerHumidity} />
                </td>
                <td className="py-1.5">
                  {reading ? (
                    <>
                      <span>{reading.recordedAt?.toLocaleString('cs-CZ')}</span>
                      <SourceBadge source={reading.source ?? CONDITIONS_SOURCE_LIVE} />
                    </>
                  ) : (
                    <span>—</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};

export default ConditionsReadingsSection;
