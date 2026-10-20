import { RecurringJobCategory, RecurringJobDto } from '../../../api/hooks/useRecurringJobs';
import {
  getCategoryLabel,
  groupJobsByCategory,
  matchesJobSearch,
} from '../recurringJobCategories';

const makeJob = (overrides: Partial<RecurringJobDto>): RecurringJobDto =>
  ({
    jobName: 'some-job',
    displayName: 'Some Job',
    description: 'Some description',
    cronExpression: '0 0 * * *',
    timeZoneId: 'Europe/Prague',
    isEnabled: true,
    category: RecurringJobCategory.Integrations,
    ...overrides,
  }) as RecurringJobDto;

describe('matchesJobSearch', () => {
  it('matches every job when the search term is empty or whitespace', () => {
    // Arrange
    const job = makeJob({ displayName: 'Daily Invoice Import' });

    // Act & Assert
    expect(matchesJobSearch(job, '')).toBe(true);
    expect(matchesJobSearch(job, '   ')).toBe(true);
  });

  it('matches the display name regardless of case', () => {
    // Arrange
    const job = makeJob({ displayName: 'Daily Invoice Import' });

    // Act & Assert
    expect(matchesJobSearch(job, 'INVOICE')).toBe(true);
  });

  it('matches the technical job name and the description', () => {
    // Arrange
    const job = makeJob({
      jobName: 'daily-comgate-czk-import',
      displayName: 'Platby',
      description: 'Imports Comgate statements',
    });

    // Act & Assert
    expect(matchesJobSearch(job, 'comgate-czk')).toBe(true);
    expect(matchesJobSearch(job, 'statements')).toBe(true);
  });

  it('returns false when no field contains the term', () => {
    // Arrange
    const job = makeJob({ displayName: 'Photobank Index', description: 'Indexes photos' });

    // Act & Assert
    expect(matchesJobSearch(job, 'invoice')).toBe(false);
  });
});

describe('groupJobsByCategory', () => {
  it('groups jobs by category in the configured display order', () => {
    // Arrange
    const jobs = [
      makeJob({ jobName: 'photobank-index', category: RecurringJobCategory.Content }),
      makeJob({ jobName: 'daily-invoice-dqt', category: RecurringJobCategory.DataQuality }),
      makeJob({ jobName: 'daily-invoice-import-czk', category: RecurringJobCategory.Finance }),
    ];

    // Act
    const groups = groupJobsByCategory(jobs, '');

    // Assert - Finance precedes Content, which precedes DataQuality
    expect(groups.map((group) => group.category)).toEqual([
      RecurringJobCategory.Finance,
      RecurringJobCategory.Content,
      RecurringJobCategory.DataQuality,
    ]);
  });

  it('omits categories that have no job matching the search term', () => {
    // Arrange
    const jobs = [
      makeJob({ jobName: 'daily-invoice-import-czk', displayName: 'Invoice Import', category: RecurringJobCategory.Finance }),
      makeJob({ jobName: 'photobank-index', displayName: 'Photobank Index', category: RecurringJobCategory.Content }),
    ];

    // Act
    const groups = groupJobsByCategory(jobs, 'invoice');

    // Assert
    expect(groups).toHaveLength(1);
    expect(groups[0].category).toBe(RecurringJobCategory.Finance);
    expect(groups[0].jobs).toHaveLength(1);
  });

  it('returns an empty array when nothing matches', () => {
    // Arrange
    const jobs = [makeJob({ displayName: 'Photobank Index' })];

    // Act
    const groups = groupJobsByCategory(jobs, 'nonexistent');

    // Assert
    expect(groups).toEqual([]);
  });

  it('places a job with a missing category into the Uncategorized group', () => {
    // Arrange - a stored job whose implementation no longer exists serialises without a category
    const jobs = [makeJob({ jobName: 'removed-job', category: undefined })];

    // Act
    const groups = groupJobsByCategory(jobs, '');

    // Assert
    expect(groups).toHaveLength(1);
    expect(groups[0].category).toBe(RecurringJobCategory.Uncategorized);
  });
});

describe('getCategoryLabel', () => {
  it('returns the Czech label for a known category', () => {
    expect(getCategoryLabel(RecurringJobCategory.Finance)).toBe('Účetnictví a platby');
  });

  it('falls back to the Uncategorized label for an unknown value', () => {
    // Arrange - the backend may add a category the frontend does not know yet
    const unknown = 'BrandNewCategory' as RecurringJobCategory;

    // Act & Assert
    expect(getCategoryLabel(unknown)).toBe('Nezařazeno');
  });
});

describe('unknown categories and diacritics', () => {
  it('ignores diacritics when matching', () => {
    // Arrange
    const job = makeJob({ displayName: 'Účtování faktur' });

    // Act & Assert
    expect(matchesJobSearch(job, 'uctovani')).toBe(true);
    expect(matchesJobSearch(makeJob({ displayName: 'Uctovani' }), 'účtování')).toBe(true);
  });

  it('puts a job with a category unknown to the client into Uncategorized instead of dropping it', () => {
    // Arrange
    const job = makeJob({
      jobName: 'future-job',
      category: 'BrandNewCategory' as RecurringJobCategory,
    });

    // Act
    const groups = groupJobsByCategory([job], '');

    // Assert
    expect(groups).toEqual([{ category: RecurringJobCategory.Uncategorized, jobs: [job] }]);
  });
});
