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
    // trackEvent identity is not stable across renders (useTelemetry returns a
    // fresh object literal each call); including it would re-fire the effect on
    // every render. The effect should only re-fire when module/screen/subScreen
    // changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [module, screen, subScreen]);
}
