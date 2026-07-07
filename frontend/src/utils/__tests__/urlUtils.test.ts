import { createFilteredUrl, isTileClickable } from '../urlUtils';

describe('createFilteredUrl', () => {
  it('should include a boolean false filter value', () => {
    const result = createFilteredUrl('/api/tiles', { enabled: false });
    expect(result).toContain('enabled=false');
  });

  it('should include a numeric zero filter value', () => {
    const result = createFilteredUrl('/api/tiles', { page: 0 });
    expect(result).toContain('page=0');
  });

  it('should exclude a null filter value', () => {
    const result = createFilteredUrl('/api/tiles', { a: null });
    expect(result).not.toContain('a=');
  });

  it('should exclude an undefined filter value', () => {
    const result = createFilteredUrl('/api/tiles', { a: undefined });
    expect(result).not.toContain('a=');
  });

  it('should exclude an empty string filter value', () => {
    const result = createFilteredUrl('/api/tiles', { a: '' });
    expect(result).not.toContain('a=');
  });

  it('should return baseUrl unchanged when all filter values are excluded', () => {
    const result = createFilteredUrl('/api/tiles', { a: null, b: undefined, c: '' });
    expect(result).toBe('/api/tiles');
    expect(result).not.toContain('?');
  });

  it('should return baseUrl unchanged when filters object is empty', () => {
    const result = createFilteredUrl('/api/tiles', {});
    expect(result).toBe('/api/tiles');
    expect(result).not.toContain('?');
  });

  it('should build a query string when at least one valid filter is present', () => {
    const result = createFilteredUrl('/api/tiles', { status: 'active' });
    expect(result).toMatch(/^\/api\/tiles\?status=active$/);
  });

  it('should include includable values and exclude excludable values in a mixed filters object', () => {
    const result = createFilteredUrl('/api/tiles', {
      enabled: false,
      page: 0,
      status: 'active',
      a: null,
      b: undefined,
      c: '',
    });

    expect(result).toContain('enabled=false');
    expect(result).toContain('page=0');
    expect(result).toContain('status=active');
    expect(result).not.toContain('a=');
    expect(result).not.toContain('b=');
    expect(result).not.toContain('c=');
  });
});

describe('isTileClickable', () => {
  it('should return true when enabled is true and filters is an empty object (documents current truthy-object behavior)', () => {
    expect(isTileClickable({ drillDown: { enabled: true, filters: {} } })).toBe(true);
  });

  it('should return false when enabled is false, regardless of filters', () => {
    expect(isTileClickable({ drillDown: { enabled: false, filters: { x: 1 } } })).toBe(false);
  });

  it('should return false when filters is missing', () => {
    expect(isTileClickable({ drillDown: { enabled: true } })).toBe(false);
  });

  it('should return false when drillDown is missing', () => {
    expect(isTileClickable({})).toBe(false);
  });

  it('should return true when enabled is true and filters has entries', () => {
    expect(isTileClickable({ drillDown: { enabled: true, filters: { x: 1 } } })).toBe(true);
  });
});
