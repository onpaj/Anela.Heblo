import React, { createContext, useContext, useEffect, useState } from "react";
import { OpenFeature, InMemoryProvider } from "@openfeature/web-sdk";
import { getAuthenticatedApiClient } from "../../api/client";

interface FeatureFlagContextValue {
  isReady: boolean;
}

const FeatureFlagContext = createContext<FeatureFlagContextValue>({ isReady: false });

async function fetchAndInitFlags(): Promise<void> {
  const client = await getAuthenticatedApiClient();
  const response = await client.featureFlags_Get();
  const flags: Record<string, boolean> = response?.flags ?? {};

  const flagsConfig = Object.fromEntries(
    Object.entries(flags).map(([key, value]) => [
      key,
      { defaultVariant: value ? "on" : "off", variants: { on: true, off: false }, disabled: false },
    ])
  );

  await OpenFeature.setProviderAndWait(new InMemoryProvider(flagsConfig));
}

interface FeatureFlagProviderProps {
  children: React.ReactNode;
}

export function FeatureFlagProvider({ children }: FeatureFlagProviderProps) {
  const [isReady, setIsReady] = useState(false);

  useEffect(() => {
    fetchAndInitFlags()
      .then(() => setIsReady(true))
      .catch((err: unknown) => {
        console.error("[FeatureFlags] Failed to load flags, using defaults:", err);
        setIsReady(true);
      });
  }, []);

  return (
    <FeatureFlagContext.Provider value={{ isReady }}>
      {children}
    </FeatureFlagContext.Provider>
  );
}

export const useFeatureFlagReady = () => useContext(FeatureFlagContext).isReady;
