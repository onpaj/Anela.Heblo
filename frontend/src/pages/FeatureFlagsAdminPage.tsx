import React from "react";
import {
  useFeatureFlagsAdmin,
  useUpsertFlagOverride,
  useClearFlagOverride,
} from "../api/hooks/useFeatureFlagsAdmin";

const FeatureFlagsAdminPage: React.FC = () => {
  const { data: flags, isLoading, error } = useFeatureFlagsAdmin();
  const upsert = useUpsertFlagOverride();
  const clear = useClearFlagOverride();

  if (isLoading) return <div className="p-8 text-gray-500">Loading flags...</div>;
  if (error) return <div className="p-8 text-red-600">Failed to load feature flags.</div>;

  return (
    <div className="p-8 max-w-4xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 mb-2">Feature Flags</h1>
      <p className="text-sm text-gray-500 mb-6">
        Overrides are stored in the database and take precedence over{" "}
        <code className="bg-gray-100 px-1 rounded">appsettings.json</code> defaults.
        Deleting an override reverts the flag to its config default.
      </p>

      <div className="space-y-3">
        {flags?.map((flag) => (
          <div
            key={flag.key}
            className="flex items-start justify-between bg-white border border-gray-200 rounded-lg p-4"
          >
            <div className="flex-1 min-w-0 mr-4">
              <div className="flex items-center gap-2">
                <code className="text-sm font-mono text-indigo-700">{flag.key}</code>
                {flag.isOverridden && (
                  <span className="text-xs bg-yellow-100 text-yellow-800 px-2 py-0.5 rounded">
                    overridden
                  </span>
                )}
              </div>
              <p className="text-sm text-gray-600 mt-1">{flag.description}</p>
              {flag.isOverridden && flag.updatedBy && (
                <p className="text-xs text-gray-400 mt-1">
                  By {flag.updatedBy}{" "}
                  {flag.updatedAt && `· ${new Date(flag.updatedAt).toLocaleString()}`}
                </p>
              )}
              <p className="text-xs text-gray-400 mt-1">
                Default: <strong>{flag.defaultValue ? "on" : "off"}</strong>
              </p>
            </div>

            <div className="flex items-center gap-3 shrink-0">
              <button
                onClick={() =>
                  upsert.mutate({ key: flag.key!, isEnabled: !flag.currentValue })
                }
                disabled={upsert.isPending}
                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 ${
                  flag.currentValue ? "bg-indigo-600" : "bg-gray-200"
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
                  disabled={clear.isPending}
                  className="text-xs text-gray-500 hover:text-red-600 underline"
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
