import { buildFeedbackListQuery } from '../useKnowledgeBase';

describe('buildFeedbackListQuery', () => {
  test('sends dates as plain yyyy-MM-dd, never ISO timestamps', () => {
    const qs = buildFeedbackListQuery({ dateFrom: '2026-10-01', dateTo: '2026-10-14' });
    expect(qs).toContain('dateFrom=2026-10-01');
    expect(qs).toContain('dateTo=2026-10-14');
    expect(qs).not.toContain('T00');
  });

  test('omits undefined and empty values', () => {
    const qs = buildFeedbackListQuery({ pageNumber: 2, problemTag: '', productTag: undefined });
    expect(qs).toBe('pageNumber=2');
  });

  test('encodes Czech tag values', () => {
    const qs = buildFeedbackListQuery({ problemTag: 'suchá pleť' });
    expect(new URLSearchParams(qs).get('problemTag')).toBe('suchá pleť');
  });

  test('keeps boolean false (hasFeedback=false is a real filter)', () => {
    expect(buildFeedbackListQuery({ hasFeedback: false })).toBe('hasFeedback=false');
  });
});
