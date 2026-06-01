import { useBooleanFlagValue } from "@openfeature/react-sdk";

export function useFeatureFlag(key: string, defaultValue: boolean = false): boolean {
  return useBooleanFlagValue(key, defaultValue);
}
