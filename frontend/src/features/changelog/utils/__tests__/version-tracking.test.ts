import {
  compareVersions,
  getVersionTracking,
  markVersionAsSeen,
  migrateVersionTracking,
  shouldShowToaster,
} from '../version-tracking';

const KEY = 'anela-heblo-version-tracking';

beforeEach(() => {
  localStorage.clear();
  jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.spyOn(console, 'error').mockImplementation(() => {});
  jest.spyOn(console, 'log').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// compareVersions
// ---------------------------------------------------------------------------

describe('compareVersions', () => {
  it('major difference wins over minor and patch', () => {
    const result = compareVersions('2.0.0', '1.9.9');
    expect(result.isNewer).toBe(true);
    expect(result.comparison).toBe(1);
  });

  it('major older returns comparison -1', () => {
    const result = compareVersions('1.9.9', '2.0.0');
    expect(result.isNewer).toBe(false);
    expect(result.comparison).toBe(-1);
  });

  it('minor difference wins when major is equal', () => {
    const result = compareVersions('1.2.0', '1.1.9');
    expect(result.isNewer).toBe(true);
    expect(result.comparison).toBe(1);
  });

  it('patch difference wins when major and minor are equal', () => {
    const result = compareVersions('1.0.2', '1.0.1');
    expect(result.isNewer).toBe(true);
    expect(result.comparison).toBe(1);
  });

  it('equal versions return comparison 0 and isNewer false', () => {
    const result = compareVersions('1.2.3', '1.2.3');
    expect(result.comparison).toBe(0);
    expect(result.isNewer).toBe(false);
  });

  it('strips leading v prefix from both versions', () => {
    const result = compareVersions('v1.2.0', 'v1.1.0');
    expect(result.isNewer).toBe(true);
    expect(result.comparison).toBe(1);
  });

  it('strips v prefix from only first version', () => {
    const result = compareVersions('v1.0.0', '1.0.0');
    expect(result.comparison).toBe(0);
  });

  it('non-numeric segment defaults to 0', () => {
    const result = compareVersions('1.alpha.0', '1.0.0');
    expect(result.comparison).toBe(0);
  });

  it('missing patch segment defaults to 0', () => {
    const result = compareVersions('1.2', '1.2.0');
    expect(result.comparison).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// getVersionTracking
// ---------------------------------------------------------------------------

describe('getVersionTracking', () => {
  it('returns defaults when localStorage is empty', () => {
    const result = getVersionTracking();
    expect(result.lastShownVersion).toBe('0.0.0');
    expect(result.seenVersions).toEqual([]);
    expect(result.lastShownAt).toBeTruthy();
  });

  it('returns defaults for invalid JSON', () => {
    localStorage.setItem(KEY, '{not-valid-json');
    const result = getVersionTracking();
    expect(result.lastShownVersion).toBe('0.0.0');
  });

  it('returns defaults when stored object is missing lastShownVersion', () => {
    const invalid = JSON.stringify({ lastShownAt: new Date().toISOString(), seenVersions: [] });
    localStorage.setItem(KEY, invalid);
    const result = getVersionTracking();
    expect(result.lastShownVersion).toBe('0.0.0');
  });

  it('returns defaults when stored object is missing seenVersions', () => {
    const invalid = JSON.stringify({ lastShownVersion: '1.0.0', lastShownAt: new Date().toISOString() });
    localStorage.setItem(KEY, invalid);
    const result = getVersionTracking();
    expect(result.lastShownVersion).toBe('0.0.0');
  });

  it('returns defaults when seenVersions is not an array', () => {
    const invalid = JSON.stringify({ lastShownVersion: '1.0.0', lastShownAt: 'x', seenVersions: 'bad' });
    localStorage.setItem(KEY, invalid);
    const result = getVersionTracking();
    expect(result.lastShownVersion).toBe('0.0.0');
  });

  it('returns parsed data for valid stored structure', () => {
    const stored = { lastShownVersion: '2.0.0', lastShownAt: '2024-01-01T00:00:00.000Z', seenVersions: ['1.0.0', '2.0.0'] };
    localStorage.setItem(KEY, JSON.stringify(stored));
    const result = getVersionTracking();
    expect(result.lastShownVersion).toBe('2.0.0');
    expect(result.seenVersions).toEqual(['1.0.0', '2.0.0']);
  });
});

// ---------------------------------------------------------------------------
// markVersionAsSeen
// ---------------------------------------------------------------------------

describe('markVersionAsSeen', () => {
  it('updates lastShownVersion when version is newer', () => {
    markVersionAsSeen('1.0.0');
    const tracking = getVersionTracking();
    expect(tracking.lastShownVersion).toBe('1.0.0');
    expect(tracking.seenVersions).toContain('1.0.0');
  });

  it('does not update lastShownVersion when version is older', () => {
    markVersionAsSeen('2.0.0');
    markVersionAsSeen('1.0.0');
    const tracking = getVersionTracking();
    expect(tracking.lastShownVersion).toBe('2.0.0');
    expect(tracking.seenVersions).toContain('1.0.0');
  });

  it('does not update lastShownVersion for equal version', () => {
    markVersionAsSeen('1.0.0');
    const firstShownAt = getVersionTracking().lastShownAt;
    markVersionAsSeen('1.0.0');
    const tracking = getVersionTracking();
    expect(tracking.lastShownVersion).toBe('1.0.0');
    expect(tracking.lastShownAt).toBe(firstShownAt);
  });

  it('does not duplicate seenVersions entry', () => {
    markVersionAsSeen('1.0.0');
    markVersionAsSeen('1.0.0');
    expect(getVersionTracking().seenVersions.filter(v => v === '1.0.0')).toHaveLength(1);
  });

  it('trims seenVersions to 10 entries after 11th push', () => {
    for (let i = 1; i <= 11; i++) {
      markVersionAsSeen(`1.0.${i}`);
    }
    const tracking = getVersionTracking();
    expect(tracking.seenVersions).toHaveLength(10);
    expect(tracking.seenVersions).not.toContain('1.0.1');
    expect(tracking.seenVersions).toContain('1.0.11');
  });
});

// ---------------------------------------------------------------------------
// shouldShowToaster
// ---------------------------------------------------------------------------

describe('shouldShowToaster', () => {
  it('returns false for version 0.0.0', () => {
    expect(shouldShowToaster('0.0.0')).toBe(false);
  });

  it('returns false for version containing "dev"', () => {
    expect(shouldShowToaster('1.0.0-dev')).toBe(false);
  });

  it('returns false for version containing "local"', () => {
    expect(shouldShowToaster('1.0.0-local')).toBe(false);
  });

  it('returns true when version is newer and not yet seen', () => {
    // Pre-seed localStorage with a clean baseline so we don't rely on the
    // module-level DEFAULT_VERSION_TRACKING whose seenVersions array can be
    // mutated by shallow-copy callers in earlier tests.
    localStorage.setItem(KEY, JSON.stringify({ lastShownVersion: '0.0.0', lastShownAt: new Date().toISOString(), seenVersions: [] }));
    expect(shouldShowToaster('1.0.0')).toBe(true);
  });

  it('returns false when version has already been seen', () => {
    markVersionAsSeen('1.0.0');
    expect(shouldShowToaster('1.0.0')).toBe(false);
  });

  it('returns false when version is not newer than last shown', () => {
    markVersionAsSeen('2.0.0');
    expect(shouldShowToaster('1.0.0')).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// migrateVersionTracking
// ---------------------------------------------------------------------------

describe('migrateVersionTracking', () => {
  it('does nothing when current tracking has non-default lastShownVersion', () => {
    const stored = { lastShownVersion: '1.0.0', lastShownAt: new Date().toISOString(), seenVersions: ['1.0.0'] };
    localStorage.setItem(KEY, JSON.stringify(stored));
    migrateVersionTracking();
    expect(getVersionTracking().lastShownVersion).toBe('1.0.0');
    expect(localStorage.getItem('changelog-version')).toBeNull();
  });

  it('migrates from changelog-version legacy key', () => {
    localStorage.setItem('changelog-version', '1.5.0');
    migrateVersionTracking();
    expect(getVersionTracking().lastShownVersion).toBe('1.5.0');
    expect(localStorage.getItem('changelog-version')).toBeNull();
  });

  it('migrates from app-version when changelog-version is absent', () => {
    localStorage.setItem('app-version', '1.3.0');
    migrateVersionTracking();
    expect(getVersionTracking().lastShownVersion).toBe('1.3.0');
    expect(localStorage.getItem('app-version')).toBeNull();
  });

  it('migrates from last-version when higher-priority keys are absent', () => {
    localStorage.setItem('last-version', '1.1.0');
    migrateVersionTracking();
    expect(getVersionTracking().lastShownVersion).toBe('1.1.0');
    expect(localStorage.getItem('last-version')).toBeNull();
  });

  it('prefers changelog-version over other legacy keys (break after first match)', () => {
    localStorage.setItem('changelog-version', '2.0.0');
    localStorage.setItem('app-version', '1.0.0');
    migrateVersionTracking();
    expect(getVersionTracking().lastShownVersion).toBe('2.0.0');
    expect(localStorage.getItem('app-version')).toBe('1.0.0');
  });
});
