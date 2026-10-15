import React from 'react';
import type { ChangeEventFilter } from '../../../../api/hooks/marketingAds/keys';
import { AD_PLATFORMS, CHANGE_ACTOR_KINDS, CHANGE_ORIGINS } from '../../../../api/hooks/marketingAds/types';
import { CHANGE_ACTOR_KIND_LABELS, CHANGE_ORIGIN_LABELS, PLATFORM_LABELS, labelFor } from '../shared/adLabels';

interface ChangeEventFiltersProps {
  filter: ChangeEventFilter;
  onChange: (filter: ChangeEventFilter) => void;
}

const SELECT =
  'mt-1 block rounded-md border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface-2 text-sm text-gray-900 dark:text-graphite-text py-1 px-2';
const LABEL = 'text-xs font-medium text-gray-600 dark:text-graphite-muted';

const toFilterValue = (value: string): string | null => (value === '' ? null : value);

interface FilterSelectProps {
  id: string;
  label: string;
  value: string | null;
  options: readonly string[];
  labels: Readonly<Record<string, string>>;
  onChange: (value: string | null) => void;
}

const FilterSelect: React.FC<FilterSelectProps> = ({ id, label, value, options, labels, onChange }) => (
  <div>
    <label htmlFor={id} className={LABEL}>
      {label}
    </label>
    <select id={id} className={SELECT} value={value ?? ''} onChange={(event) => onChange(toFilterValue(event.target.value))}>
      <option value="">Vše</option>
      {options.map((option) => (
        <option key={option} value={option}>
          {labelFor(labels, option)}
        </option>
      ))}
    </select>
  </div>
);

const ChangeEventFilters: React.FC<ChangeEventFiltersProps> = ({ filter, onChange }) => (
  <div className="flex flex-wrap gap-4">
    <FilterSelect
      id="ads-change-platform"
      label="Platforma"
      value={filter.platform}
      options={AD_PLATFORMS}
      labels={PLATFORM_LABELS}
      onChange={(platform) => onChange({ ...filter, platform })}
    />
    <FilterSelect
      id="ads-change-origin"
      label="Původ"
      value={filter.origin}
      options={CHANGE_ORIGINS}
      labels={CHANGE_ORIGIN_LABELS}
      onChange={(origin) => onChange({ ...filter, origin })}
    />
    <FilterSelect
      id="ads-change-actor"
      label="Aktér"
      value={filter.actorKind}
      options={CHANGE_ACTOR_KINDS}
      labels={CHANGE_ACTOR_KIND_LABELS}
      onChange={(actorKind) => onChange({ ...filter, actorKind })}
    />
  </div>
);

export default ChangeEventFilters;
