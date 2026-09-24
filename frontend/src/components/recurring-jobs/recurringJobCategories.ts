import { RecurringJobCategory, RecurringJobDto } from '../../api/hooks/useRecurringJobs';

/** Czech labels for the job categories shown as group headers. */
export const CATEGORY_LABELS: Record<RecurringJobCategory, string> = {
  [RecurringJobCategory.Finance]: 'Účetnictví a platby',
  [RecurringJobCategory.Catalog]: 'Katalog a ceny',
  [RecurringJobCategory.Warehouse]: 'Sklad a expedice',
  [RecurringJobCategory.Marketing]: 'Marketing',
  [RecurringJobCategory.Content]: 'Obsah a AI',
  [RecurringJobCategory.DataQuality]: 'Kontroly dat',
  [RecurringJobCategory.Attendance]: 'Docházka',
  [RecurringJobCategory.Integrations]: 'Integrace',
  [RecurringJobCategory.Uncategorized]: 'Nezařazeno',
};

/** Order in which category groups are rendered. Unknown categories sort last. */
export const CATEGORY_DISPLAY_ORDER: RecurringJobCategory[] = [
  RecurringJobCategory.Finance,
  RecurringJobCategory.Catalog,
  RecurringJobCategory.Warehouse,
  RecurringJobCategory.Marketing,
  RecurringJobCategory.Content,
  RecurringJobCategory.DataQuality,
  RecurringJobCategory.Attendance,
  RecurringJobCategory.Integrations,
  RecurringJobCategory.Uncategorized,
];

export const getCategoryLabel = (category: RecurringJobCategory): string =>
  CATEGORY_LABELS[category] ?? CATEGORY_LABELS[RecurringJobCategory.Uncategorized];

/**
 * Maps a category the client doesn't know (e.g. one added on the backend before the
 * client was regenerated) to Uncategorized, so its jobs are still rendered.
 */
export const resolveCategory = (category?: RecurringJobCategory): RecurringJobCategory =>
  category && category in CATEGORY_LABELS ? category : RecurringJobCategory.Uncategorized;

export interface RecurringJobCategoryGroup {
  category: RecurringJobCategory;
  jobs: RecurringJobDto[];
}

/** Lower-cases and strips diacritics, so "uctovani" matches "Účtování". */
const normalizeForSearch = (value: string): string =>
  value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

/**
 * Case- and diacritics-insensitive match of the search term against the job's display name,
 * technical name and description. An empty term matches everything.
 */
export const matchesJobSearch = (job: RecurringJobDto, searchTerm: string): boolean => {
  const term = normalizeForSearch(searchTerm.trim());
  if (!term) return true;

  return [job.displayName, job.jobName, job.description]
    .some((field) => field != null && normalizeForSearch(field).includes(term));
};

/**
 * Filters jobs by the search term and groups the survivors by category,
 * in CATEGORY_DISPLAY_ORDER. Categories with no matching job are omitted.
 */
export const groupJobsByCategory = (
  jobs: RecurringJobDto[],
  searchTerm: string
): RecurringJobCategoryGroup[] => {
  const matching = jobs.filter((job) => matchesJobSearch(job, searchTerm));

  return CATEGORY_DISPLAY_ORDER
    .map((category) => ({
      category,
      jobs: matching.filter(
        (job) => resolveCategory(job.category) === category
      ),
    }))
    .filter((group) => group.jobs.length > 0);
};
