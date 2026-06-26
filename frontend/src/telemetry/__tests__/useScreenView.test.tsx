import { renderHook } from '@testing-library/react';
import type { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { useScreenView } from '../useScreenView';
import * as appInsightsModule from '../appInsights';

const mockTrackEvent = jest.fn();
const mockAI = { trackEvent: mockTrackEvent };

describe('useScreenView', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    jest.spyOn(appInsightsModule, 'getAppInsights').mockReturnValue(
      mockAI as unknown as ApplicationInsights,
    );
  });

  it('fires ScreenViewed on mount with module and screen', () => {
    renderHook(() => useScreenView('Dashboard', 'Dashboard'));

    expect(mockTrackEvent).toHaveBeenCalledTimes(1);
    expect(mockTrackEvent).toHaveBeenCalledWith(
      { name: 'ScreenViewed' },
      { module: 'Dashboard', screen: 'Dashboard' },
    );
  });

  it('includes subScreen when provided', () => {
    renderHook(() => useScreenView('Catalog', 'CatalogDetail', 'MarginsTab'));

    expect(mockTrackEvent).toHaveBeenCalledWith(
      { name: 'ScreenViewed' },
      { module: 'Catalog', screen: 'CatalogDetail', subScreen: 'MarginsTab' },
    );
  });

  it('re-fires when subScreen changes', () => {
    const { rerender } = renderHook(
      ({ tab }: { tab: string }) => useScreenView('Catalog', 'CatalogDetail', tab),
      { initialProps: { tab: 'BasicTab' } },
    );

    expect(mockTrackEvent).toHaveBeenCalledTimes(1);

    rerender({ tab: 'MarginsTab' });

    expect(mockTrackEvent).toHaveBeenCalledTimes(2);
    expect(mockTrackEvent).toHaveBeenLastCalledWith(
      { name: 'ScreenViewed' },
      { module: 'Catalog', screen: 'CatalogDetail', subScreen: 'MarginsTab' },
    );
  });

  it('does not re-fire when subScreen is unchanged across renders', () => {
    const { rerender } = renderHook(
      ({ tab }: { tab: string }) => useScreenView('Catalog', 'CatalogDetail', tab),
      { initialProps: { tab: 'BasicTab' } },
    );
    rerender({ tab: 'BasicTab' });

    expect(mockTrackEvent).toHaveBeenCalledTimes(1);
  });

  it('does not throw when AI instance is null', () => {
    jest.spyOn(appInsightsModule, 'getAppInsights').mockReturnValue(null);

    expect(() =>
      renderHook(() => useScreenView('Dashboard', 'Dashboard')),
    ).not.toThrow();
  });
});
