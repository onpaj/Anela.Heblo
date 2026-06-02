jest.mock('../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

import {
  DASHBOARD_DRILLDOWN_ROUTES,
  resolveDrillDown,
} from '../drillDownRoutes';

describe('drillDownRoutes', () => {
  const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

  beforeEach(() => {
    warnSpy.mockClear();
  });

  afterAll(() => {
    warnSpy.mockRestore();
  });

  it('registry contains the known route keys', () => {
    expect(DASHBOARD_DRILLDOWN_ROUTES.dataQuality).toEqual({
      type: 'react-router',
      path: '/automation/data-quality',
    });
    expect(DASHBOARD_DRILLDOWN_ROUTES.hangfireFailedJobs).toEqual({
      type: 'external',
      path: '/hangfire/jobs/failed',
    });
  });

  it('resolves a react-router key to the registered path with strategy "react-router"', () => {
    const result = resolveDrillDown({ routeKey: 'dataQuality', enabled: true });

    expect(result).toEqual({
      url: '/automation/data-quality',
      strategy: 'react-router',
    });
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('resolves an external key by prepending apiUrl and strategy "external"', () => {
    const result = resolveDrillDown({ routeKey: 'hangfireFailedJobs', enabled: true });

    expect(result).toEqual({
      url: 'http://localhost:5001/hangfire/jobs/failed',
      strategy: 'external',
    });
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('returns null and warns for an unknown route key', () => {
    const result = resolveDrillDown({ routeKey: 'someUnknownKey', enabled: true });

    expect(result).toBeNull();
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('someUnknownKey'),
    );
  });

  it('returns null when the drill-down is disabled', () => {
    const result = resolveDrillDown({ routeKey: 'dataQuality', enabled: false });

    expect(result).toBeNull();
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('returns null when the drill-down is undefined', () => {
    const result = resolveDrillDown(undefined);

    expect(result).toBeNull();
    expect(warnSpy).not.toHaveBeenCalled();
  });
});
