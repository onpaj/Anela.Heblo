/**
 * Version tracking utilities for localStorage management
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import { VersionTracking, VersionTrackingError, VersionCompareResult } from '../types';

/**
 * LocalStorage key for version tracking
 */
const VERSION_TRACKING_KEY = 'anela-heblo-version-tracking';

/**
 * Default version tracking data
 */
const DEFAULT_VERSION_TRACKING: VersionTracking = {
  lastShownVersion: '0.0.0',
  lastShownAt: new Date().toISOString(),
  seenVersions: [],
};

/**
 * Parse semantic version string into comparable parts
 */
function parseVersion(version: string): [number, number, number] {
  const cleaned = version.replace(/^v/, ''); // Remove 'v' prefix if present
  const parts = cleaned.split('.').map(part => {
    const num = parseInt(part, 10);
    return isNaN(num) ? 0 : num;
  });
  
  return [
    parts[0] || 0, // major
    parts[1] || 0, // minor
    parts[2] || 0, // patch
  ];
}

/**
 * Compare two semantic versions
 * @param version1 First version to compare
 * @param version2 Second version to compare
 * @returns Comparison result object
 */
export function compareVersions(version1: string, version2: string): VersionCompareResult {
  try {
    const [major1, minor1, patch1] = parseVersion(version1);
    const [major2, minor2, patch2] = parseVersion(version2);
    
    // Compare major version
    if (major1 !== major2) {
      const comparison = major1 > major2 ? 1 : -1;
      return {
        isNewer: comparison === 1,
        comparison,
      };
    }
    
    // Compare minor version
    if (minor1 !== minor2) {
      const comparison = minor1 > minor2 ? 1 : -1;
      return {
        isNewer: comparison === 1,
        comparison,
      };
    }
    
    // Compare patch version
    if (patch1 !== patch2) {
      const comparison = patch1 > patch2 ? 1 : -1;
      return {
        isNewer: comparison === 1,
        comparison,
      };
    }
    
    // Versions are equal
    return {
      isNewer: false,
      comparison: 0,
    };
  } catch (error) {
    throw new VersionTrackingError(
      `Failed to compare versions: ${version1} vs ${version2}`,
      error instanceof Error ? error : undefined
    );
  }
}

/**
 * Get version tracking data from localStorage
 */
export function getVersionTracking(): VersionTracking {
  try {
    const stored = localStorage.getItem(VERSION_TRACKING_KEY);
    if (!stored) {
      return { ...DEFAULT_VERSION_TRACKING };
    }
    
    const parsed = JSON.parse(stored) as VersionTracking;
    
    // Validate structure
    if (!parsed.lastShownVersion || !parsed.lastShownAt || !Array.isArray(parsed.seenVersions)) {
      console.warn('Invalid version tracking data found, resetting to defaults');
      return { ...DEFAULT_VERSION_TRACKING };
    }
    
    return parsed;
  } catch (error) {
    console.error('Failed to load version tracking data:', error);
    return { ...DEFAULT_VERSION_TRACKING };
  }
}

/**
 * Save version tracking data to localStorage
 */
export function saveVersionTracking(tracking: VersionTracking): void {
  try {
    const serialized = JSON.stringify(tracking);
    localStorage.setItem(VERSION_TRACKING_KEY, serialized);
  } catch (error) {
    throw new VersionTrackingError(
      'Failed to save version tracking data',
      error instanceof Error ? error : undefined
    );
  }
}

/**
 * Check if a version is new for the user
 * @param version Version to check
 * @returns True if version is newer than last shown version
 */
export function isNewVersion(version: string): boolean {
  try {
    const tracking = getVersionTracking();
    const comparison = compareVersions(version, tracking.lastShownVersion);
    return comparison.isNewer;
  } catch (error) {
    console.error('Failed to check if version is new:', error);
    // Default to showing toaster on error (better UX)
    return true;
  }
}

/**
 * Check if user has seen a specific version
 * @param version Version to check
 * @returns True if user has seen this version before
 */
export function hasSeenVersion(version: string): boolean {
  try {
    const tracking = getVersionTracking();
    return tracking.seenVersions.includes(version);
  } catch (error) {
    console.error('Failed to check if version was seen:', error);
    return false;
  }
}

/**
 * Mark a version as seen by the user
 * @param version Version to mark as seen
 */
export function markVersionAsSeen(version: string): void {
  try {
    const tracking = getVersionTracking();
    
    // Update last shown version if this is newer
    const comparison = compareVersions(version, tracking.lastShownVersion);
    if (comparison.isNewer || comparison.comparison === 0) {
      tracking.lastShownVersion = version;
      tracking.lastShownAt = new Date().toISOString();
    }
    
    // Add to seen versions if not already present
    if (!tracking.seenVersions.includes(version)) {
      tracking.seenVersions.push(version);
      
      // Keep only last 10 seen versions to prevent localStorage bloat
      if (tracking.seenVersions.length > 10) {
        tracking.seenVersions = tracking.seenVersions.slice(-10);
      }
    }
    
    saveVersionTracking(tracking);
  } catch (error) {
    throw new VersionTrackingError(
      `Failed to mark version as seen: ${version}`,
      error instanceof Error ? error : undefined
    );
  }
}

/**
 * Reset version tracking (useful for testing)
 */
export function resetVersionTracking(): void {
  try {
    localStorage.removeItem(VERSION_TRACKING_KEY);
  } catch (error) {
    throw new VersionTrackingError(
      'Failed to reset version tracking',
      error instanceof Error ? error : undefined
    );
  }
}

/**
 * Get last shown version
 */
export function getLastShownVersion(): string {
  try {
    const tracking = getVersionTracking();
    return tracking.lastShownVersion;
  } catch (error) {
    console.error('Failed to get last shown version:', error);
    return '0.0.0';
  }
}

/**
 * Get all seen versions
 */
export function getSeenVersions(): string[] {
  try {
    const tracking = getVersionTracking();
    return [...tracking.seenVersions];
  } catch (error) {
    console.error('Failed to get seen versions:', error);
    return [];
  }
}

/**
 * Check if we should show changelog toaster for current version
 * @param currentVersion Current application version
 * @returns True if toaster should be shown
 */
export function shouldShowToaster(currentVersion: string): boolean {
  try {
    // Don't show for development versions
    if (currentVersion === '0.0.0' || currentVersion.includes('dev') || currentVersion.includes('local')) {
      return false;
    }
    
    return isNewVersion(currentVersion) && !hasSeenVersion(currentVersion);
  } catch (error) {
    console.error('Failed to determine if toaster should be shown:', error);
    return false;
  }
}

/**
 * Utility to clean up old localStorage data (migration helper)
 */
export function migrateVersionTracking(): void {
  try {
    // Check for old format data and migrate if necessary
    const currentData = getVersionTracking();
    
    // If we have default data, check for any old keys to migrate
    if (currentData.lastShownVersion === '0.0.0') {
      // Look for any old version tracking keys
      const oldKeys = ['changelog-version', 'app-version', 'last-version'];
      
      for (const key of oldKeys) {
        const oldValue = localStorage.getItem(key);
        if (oldValue) {
          try {
            // Try to parse and migrate
            const oldTracking = {
              lastShownVersion: oldValue,
              lastShownAt: new Date().toISOString(),
              seenVersions: [oldValue],
            };
            
            saveVersionTracking(oldTracking);
            localStorage.removeItem(key);
            console.log(`Migrated version tracking from ${key}: ${oldValue}`);
            break;
          } catch (migrationError) {
            console.warn(`Failed to migrate from ${key}:`, migrationError);
          }
        }
      }
    }
  } catch (error) {
    console.warn('Version tracking migration failed:', error);
  }
}