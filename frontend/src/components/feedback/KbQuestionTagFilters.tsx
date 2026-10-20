import React from 'react';
import type { TagCount } from './ragFeedbackTypes';
import type { KbFeedbackParams } from './types';

interface KbQuestionTagFiltersProps {
  dateFrom?: string;
  dateTo?: string;
  problemTag?: string;
  productTag?: string;
  problemOptions: TagCount[];
  productOptions: TagCount[];
  onChange: (update: Partial<KbFeedbackParams>) => void;
}

const controlClass =
  'border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500';
const labelClass = 'text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap';

const emptyToUndefined = (value: string): string | undefined => (value === '' ? undefined : value);

// A selected tag must stay selectable even if the chosen date range no longer contains it.
const withSelected = (options: TagCount[], selected?: string): TagCount[] =>
  selected && !options.some((o) => o.tag === selected)
    ? [...options, { tag: selected, label: selected, count: 0 }]
    : options;

interface TagSelectProps {
  id: string;
  label: string;
  value?: string;
  options: TagCount[];
  onSelect: (tag: string | undefined) => void;
}

function TagSelect({ id, label, value, options, onSelect }: TagSelectProps) {
  return (
    <div className="flex items-center gap-2">
      <label htmlFor={id} className={labelClass}>{label}</label>
      <select id={id} value={value ?? ''} onChange={(e) => onSelect(emptyToUndefined(e.target.value))} className={controlClass}>
        <option value="">Vše</option>
        {withSelected(options, value).map((o) => (
          <option key={o.tag} value={o.tag}>{`${o.label} (${o.count})`}</option>
        ))}
      </select>
    </div>
  );
}

function KbQuestionTagFilters({
  dateFrom,
  dateTo,
  problemTag,
  productTag,
  problemOptions,
  productOptions,
  onChange,
}: KbQuestionTagFiltersProps) {
  return (
    <div className="flex flex-wrap gap-3 items-center">
      <div className="flex items-center gap-2">
        <label htmlFor="kb-filter-date-from" className={labelClass}>Od:</label>
        <input id="kb-filter-date-from" type="date" value={dateFrom ?? ''} className={controlClass}
          onChange={(e) => onChange({ dateFrom: emptyToUndefined(e.target.value) })} />
      </div>
      <div className="flex items-center gap-2">
        <label htmlFor="kb-filter-date-to" className={labelClass}>Do:</label>
        <input id="kb-filter-date-to" type="date" value={dateTo ?? ''} className={controlClass}
          onChange={(e) => onChange({ dateTo: emptyToUndefined(e.target.value) })} />
      </div>
      <TagSelect id="kb-filter-problem" label="Problém:" value={problemTag} options={problemOptions}
        onSelect={(tag) => onChange({ problemTag: tag })} />
      <TagSelect id="kb-filter-product" label="Produkt:" value={productTag} options={productOptions}
        onSelect={(tag) => onChange({ productTag: tag })} />
    </div>
  );
}

export default KbQuestionTagFilters;
