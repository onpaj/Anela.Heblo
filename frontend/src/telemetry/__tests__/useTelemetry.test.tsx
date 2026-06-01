import { renderHook, act } from '@testing-library/react';
import type { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { useTelemetry } from '../useTelemetry';
import * as appInsightsModule from '../appInsights';

const mockTrackEvent = jest.fn();
const mockTrackException = jest.fn();
const mockTrackMetric = jest.fn();

const mockAI = {
  trackEvent: mockTrackEvent,
  trackException: mockTrackException,
  trackMetric: mockTrackMetric,
};

describe('useTelemetry', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('when AI instance is available', () => {
    beforeEach(() => {
      jest.spyOn(appInsightsModule, 'getAppInsights').mockReturnValue(mockAI as unknown as ApplicationInsights);
    });

    it('trackEvent calls ai.trackEvent with name and merged properties+metrics', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackEvent('DashboardTileClicked', { tileId: 'tile-1' });
      });

      expect(mockTrackEvent).toHaveBeenCalledWith(
        { name: 'DashboardTileClicked' },
        { tileId: 'tile-1' }
      );
    });

    it('trackEvent merges properties and metrics into properties envelope', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackEvent(
          'PhotobankBulkTagApplied',
          { tagCount: '3' },
          { photoCount: 12 }
        );
      });

      expect(mockTrackEvent).toHaveBeenCalledWith(
        { name: 'PhotobankBulkTagApplied' },
        { tagCount: '3', photoCount: 12 }
      );
    });

    it('trackEvent works with no properties or metrics', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackEvent('ManufactureOrderCreated');
      });

      expect(mockTrackEvent).toHaveBeenCalledWith(
        { name: 'ManufactureOrderCreated' },
        {}
      );
    });

    it('trackException calls ai.trackException', () => {
      const { result } = renderHook(() => useTelemetry());
      const error = new Error('test error');

      act(() => {
        result.current.trackException(error, { context: 'test' });
      });

      expect(mockTrackException).toHaveBeenCalledWith({
        exception: error,
        properties: { context: 'test' },
      });
    });

    it('trackMetric calls ai.trackMetric', () => {
      const { result } = renderHook(() => useTelemetry());

      act(() => {
        result.current.trackMetric('loadTime', 250);
      });

      expect(mockTrackMetric).toHaveBeenCalledWith({ name: 'loadTime', average: 250 });
    });
  });

  describe('when AI instance is null (NoOp mode)', () => {
    beforeEach(() => {
      jest.spyOn(appInsightsModule, 'getAppInsights').mockReturnValue(null);
    });

    it('trackEvent does not throw when AI is null', () => {
      const { result } = renderHook(() => useTelemetry());

      expect(() => {
        act(() => {
          result.current.trackEvent('DashboardTileClicked', { tileId: 'tile-1' });
        });
      }).not.toThrow();
    });

    it('trackException does not throw when AI is null', () => {
      const { result } = renderHook(() => useTelemetry());

      expect(() => {
        act(() => {
          result.current.trackException(new Error('test'));
        });
      }).not.toThrow();
    });

    it('trackMetric does not throw when AI is null', () => {
      const { result } = renderHook(() => useTelemetry());

      expect(() => {
        act(() => {
          result.current.trackMetric('loadTime', 100);
        });
      }).not.toThrow();
    });
  });
});
