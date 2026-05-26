import { useEffect } from 'react';
import { useTelemetry } from './useTelemetry';
import type { ScreenModule } from './screenModules';

export function useScreenView(
  module: ScreenModule,
  screen: string,
  subScreen?: string,
): void {
  const { trackEvent } = useTelemetry();

  useEffect(() => {
    const properties: Record<string, string> = { module, screen };
    if (subScreen !== undefined) {
      properties.subScreen = subScreen;
    }
    trackEvent('ScreenViewed', properties);
    // trackEvent is stable per useTelemetry contract; intentionally excluded
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [module, screen, subScreen]);
}
