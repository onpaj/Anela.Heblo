import { getAppInsights } from './appInsights';
import type { TelemetryEventName } from './events';

type Props = Record<string, string | number | boolean>;
type Metrics = Record<string, number>;

export function useTelemetry() {
  return {
    trackEvent: (name: TelemetryEventName, properties?: Props, metrics?: Metrics) => {
      getAppInsights()?.trackEvent({ name }, { ...properties, ...metrics });
    },
    trackException: (error: Error, properties?: Props) => {
      getAppInsights()?.trackException({ exception: error, properties });
    },
    trackMetric: (name: string, value: number) => {
      getAppInsights()?.trackMetric({ name, average: value });
    },
  };
}
