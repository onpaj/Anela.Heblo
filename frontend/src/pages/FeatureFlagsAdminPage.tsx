import React from "react";
import {
  useFeatureFlagsAdmin,
  useUpsertFlagOverride,
  useClearFlagOverride,
} from "../api/hooks/useFeatureFlagsAdmin";
import { useTelemetry } from "../telemetry/useTelemetry";
import { useScreenView } from "../telemetry/useScreenView";

const FeatureFlagsAdminPage: React.FC = () => {
  const { data: flags, isLoading, error } = useFeatureFlagsAdmin();
  const upsert = useUpsertFlagOverride();
  const clear = useClearFlagOverride();
  const isBusy = upsert.isPending || clear.isPending;
  const { trackEvent } = useTelemetry();

  useScreenView('Admin', 'FeatureFlagsAdmin');

  if (isLoading) return <div className="p-8 text-gray-500 dark:text-graphite-muted">Loading flags...</div>;
  if (error) return <div className="p-8 text-red-600 dark:text-red-400">Failed to load feature flags.</div>;

  return (
    <div className="p-8 max-w-4xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 dark:text-graphite-text mb-2">Feature Flags</h1>
      <p className="text-sm text-gray-500 dark:text-graphite-muted mb-6">
        Overrides are stored in the database and take precedence over{" "}
        <code className="bg-gray-100 dark:bg-graphite-surface-2 px-1 rounded">appsettings.json</code> defaults.
        Deleting an override reverts the flag to its config default.
      </p>

      <div className="space-y-3">
        {flags?.map((flag) => (
          <div
            key={flag.key}
            className="flex items-start justify-between bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-4"
          >
            <div className="flex-1 min-w-0 mr-4">
              <div className="flex items-center gap-2">
                <code className="text-sm font-mono text-indigo-700 dark:text-graphite-accent">{flag.key}</code>
                {flag.isOverridden && (
                  <span className="text-xs bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300 px-2 py-0.5 rounded">
                    overridden
                  </span>
                )}
              </div>
              <p className="text-sm text-gray-600 dark:text-graphite-muted mt-1">{flag.description}</p>
              {flag.isOverridden && flag.updatedBy && (
                <p className="text-xs text-gray-400 dark:text-graphite-faint mt-1">
                  By {flag.updatedBy}{" "}
                  {flag.updatedAt && `· ${new Date(flag.updatedAt).toLocaleString()}`}
                </p>
              )}
              <p className="text-xs text-gray-400 dark:text-graphite-faint mt-1">
                Default: <strong>{flag.defaultValue ? "on" : "off"}</strong>
              </p>
            </div>

            <div className="flex items-center gap-3 shrink-0">
              <button
                onClick={() => {
                  const newEnabled = !flag.currentValue;
                  trackEvent('FeatureFlagToggled', {
                    flagKey: flag.key ?? '',
                    enabled: String(newEnabled),
                  });
                  upsert.mutate({ key: flag.key!, isEnabled: newEnabled });
                }}
                disabled={isBusy}
                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 ${
                  flag.currentValue ? "bg-indigo-600" : "bg-gray-200 dark:bg-graphite-hover"
                }`}
                aria-label={`Toggle ${flag.key}`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                    flag.currentValue ? "translate-x-6" : "translate-x-1"
                  }`}
                />
              </button>

              {flag.isOverridden && (
                <button
                  onClick={() => clear.mutate(flag.key!)}
                  disabled={isBusy}
                  className="text-xs text-gray-500 dark:text-graphite-muted hover:text-red-600 underline"
                >
                  Reset
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default FeatureFlagsAdminPage;
