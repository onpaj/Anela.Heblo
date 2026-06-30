import { VersionService } from '../versionService';
import { getAuthenticatedApiClient } from '../../api/client';

// ---------------------------------------------------------------------------
// Module-level mock — must be hoisted to top of file (jest transforms this)
// ---------------------------------------------------------------------------
jest.mock('../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

// Typed handle on the mock so we can configure return values per-test
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.Mock;

// ---------------------------------------------------------------------------
// Shared mock API client factory
// ---------------------------------------------------------------------------
function makeMockApiClient(version: string) {
  return {
    configuration_GetConfiguration: jest.fn().mockResolvedValue({
      version,
      environment: 'test',
      useMockAuth: false,
      timestamp: new Date('2024-01-01T00:00:00Z'),
    }),
  };
}

// ---------------------------------------------------------------------------
// Global setup — clean storage before every test
// ---------------------------------------------------------------------------
beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  jest.clearAllMocks();
});

// ===========================================================================
// hasNewVersion
// ===========================================================================
describe('hasNewVersion', () => {
  let service: VersionService;

  beforeEach(() => {
    service = new VersionService();
  });

  // FR-1: first-time call — no stored version
  it('FR-1: returns hasUpdate=false and stores the remote version on first call', async () => {
    const remoteVersion = '1.0.0';
    mockGetAuthenticatedApiClient.mockReturnValue(makeMockApiClient(remoteVersion));

    const result = await service.hasNewVersion();

    expect(result.hasUpdate).toBe(false);
    expect(result.newVersion).toBeUndefined();
    expect(localStorage.getItem('app_version')).toBe(remoteVersion);
  });

  // FR-2: same version already stored
  it('FR-2: returns hasUpdate=false when stored version equals remote version', async () => {
    const version = '1.0.0';
    localStorage.setItem('app_version', version);
    mockGetAuthenticatedApiClient.mockReturnValue(makeMockApiClient(version));

    const result = await service.hasNewVersion();

    expect(result.hasUpdate).toBe(false);
    expect(result.newVersion).toBeUndefined();
  });

  // FR-3: new version, not yet notified
  it('FR-3: returns hasUpdate=true with version strings when a new unnotified version is available', async () => {
    const storedVersion = '1.0.0';
    const remoteVersion = '1.1.0';
    localStorage.setItem('app_version', storedVersion);
    mockGetAuthenticatedApiClient.mockReturnValue(makeMockApiClient(remoteVersion));

    const result = await service.hasNewVersion();

    expect(result.hasUpdate).toBe(true);
    expect(result.newVersion).toBe(remoteVersion);
    expect(result.currentVersion).toBe(storedVersion);
  });

  // FR-4: new version but already notified
  it('FR-4: returns hasUpdate=false when new version was already notified', async () => {
    const storedVersion = '1.0.0';
    const remoteVersion = '1.1.0';
    localStorage.setItem('app_version', storedVersion);
    localStorage.setItem('notified_versions', JSON.stringify([remoteVersion]));
    mockGetAuthenticatedApiClient.mockReturnValue(makeMockApiClient(remoteVersion));

    const result = await service.hasNewVersion();

    expect(result.hasUpdate).toBe(false);
    expect(result.newVersion).toBeUndefined();
  });

  // FR-9: error path — checkVersion throws
  it('FR-9: returns hasUpdate=false without throwing when checkVersion throws', async () => {
    mockGetAuthenticatedApiClient.mockReturnValue({
      configuration_GetConfiguration: jest.fn().mockRejectedValue(new Error('network error')),
    });

    const result = await service.hasNewVersion();

    expect(result.hasUpdate).toBe(false);
    expect(result.newVersion).toBeUndefined();
  });
});

// ===========================================================================
// markVersionAsNotified rolling cap (FR-5)
// ===========================================================================
describe('markVersionAsNotified rolling cap', () => {
  it('FR-5: keeps at most 10 notified versions and evicts the first when 11 are added', () => {
    const service = new VersionService();

    const versions = Array.from({ length: 11 }, (_, i) => `1.0.${i}`);
    versions.forEach((v) => (service as any).markVersionAsNotified(v));

    const stored: string[] = JSON.parse(
      localStorage.getItem('notified_versions') as string,
    );

    expect(stored).toHaveLength(10);
    expect(stored).not.toContain(versions[0]);
    versions.slice(1).forEach((v) => expect(stored).toContain(v));
  });
});

// ===========================================================================
// updateToNewVersion / clearCaches (FR-6)
// ===========================================================================
describe('updateToNewVersion', () => {
  let service: VersionService;

  beforeEach(() => {
    service = new VersionService();
    Object.defineProperty(window, 'location', {
      configurable: true,
      writable: true,
      value: { href: '' },
    });
  });

  it('FR-6: stores new version, clears notified_versions and sessionStorage, and navigates to /', () => {
    const newVersion = '2.0.0';
    localStorage.setItem('app_version', '1.0.0');
    localStorage.setItem('notified_versions', JSON.stringify(['1.0.0']));
    sessionStorage.setItem('someKey', 'someValue');

    service.updateToNewVersion(newVersion);

    expect(localStorage.getItem('app_version')).toBe(newVersion);
    expect(localStorage.getItem('notified_versions')).toBeNull();
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).toBe('/');
  });
});

// ===========================================================================
// startPeriodicCheck (FR-7, FR-8) — fake timers scoped to this block
// ===========================================================================
describe('startPeriodicCheck', () => {
  let service: VersionService;

  beforeEach(() => {
    service = new VersionService();
    jest.useFakeTimers();
  });

  afterEach(() => {
    service.stopPeriodicCheck();
    jest.useRealTimers();
  });

  // Helper: flush all pending microtasks
  async function flushAllMicrotasks() {
    for (let i = 0; i < 10; i++) {
      await Promise.resolve();
    }
  }

  // FR-7: callback fired on new version
  it('FR-7: calls onNewVersionDetected and marks the version as notified when a new version is found', async () => {
    const storedVersion = '1.0.0';
    const remoteVersion = '1.1.0';
    localStorage.setItem('app_version', storedVersion);
    mockGetAuthenticatedApiClient.mockReturnValue(makeMockApiClient(remoteVersion));

    const onNewVersionDetected = jest.fn();
    service.startPeriodicCheck(onNewVersionDetected);

    jest.advanceTimersByTime(300000);
    await flushAllMicrotasks();

    expect(onNewVersionDetected).toHaveBeenCalledTimes(1);
    const notified: string[] = JSON.parse(
      localStorage.getItem('notified_versions') as string,
    );
    expect(notified).toContain(remoteVersion);
  });

  // FR-8: callback not fired when version already notified
  it('FR-8: does not call onNewVersionDetected when the new version was already notified', async () => {
    const storedVersion = '1.0.0';
    const remoteVersion = '1.1.0';
    localStorage.setItem('app_version', storedVersion);
    localStorage.setItem('notified_versions', JSON.stringify([remoteVersion]));
    mockGetAuthenticatedApiClient.mockReturnValue(makeMockApiClient(remoteVersion));

    const onNewVersionDetected = jest.fn();
    service.startPeriodicCheck(onNewVersionDetected);

    jest.advanceTimersByTime(300000);
    await flushAllMicrotasks();

    expect(onNewVersionDetected).toHaveBeenCalledTimes(0);
  });
});
