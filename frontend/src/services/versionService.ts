import { getAuthenticatedApiClient } from '../api/client';

export interface VersionInfo {
  version: string;
  environment: string;
  useMockAuth: boolean;
  timestamp: string;
}

export class VersionService {
  private static readonly STORAGE_KEY = 'app_version';
  private static readonly NOTIFIED_VERSIONS_KEY = 'notified_versions';
  private static readonly CHECK_INTERVAL = 5 * 60 * 1000; // 5 minutes

  private lastCheckedVersion: string | null = null;
  private checkInterval: NodeJS.Timeout | null = null;

  /**
   * Get the currently stored version from localStorage
   */
  public getCurrentStoredVersion(): string | null {
    try {
      const storedVersion = localStorage.getItem(VersionService.STORAGE_KEY);
      return storedVersion;
    } catch (error) {
      console.warn('Failed to read stored version:', error);
      return null;
    }
  }

  /**
   * Store the version in localStorage
   */
  public storeVersion(version: string): void {
    try {
      localStorage.setItem(VersionService.STORAGE_KEY, version);
      this.lastCheckedVersion = version;
    } catch (error) {
      console.warn('Failed to store version:', error);
    }
  }

  /**
   * Get the list of notified versions from localStorage
   */
  private getNotifiedVersions(): string[] {
    try {
      const notifiedVersions = localStorage.getItem(VersionService.NOTIFIED_VERSIONS_KEY);
      return notifiedVersions ? JSON.parse(notifiedVersions) : [];
    } catch (error) {
      console.warn('Failed to read notified versions:', error);
      return [];
    }
  }

  /**
   * Mark a version as notified to prevent showing the same notification repeatedly
   */
  private markVersionAsNotified(version: string): void {
    try {
      const notifiedVersions = this.getNotifiedVersions();
      if (!notifiedVersions.includes(version)) {
        notifiedVersions.push(version);
        // Keep only the last 10 notified versions to prevent storage bloat
        const trimmedVersions = notifiedVersions.slice(-10);
        localStorage.setItem(VersionService.NOTIFIED_VERSIONS_KEY, JSON.stringify(trimmedVersions));
      }
    } catch (error) {
      console.warn('Failed to mark version as notified:', error);
    }
  }

  /**
   * Check if a version has already been notified to the user
   */
  private isVersionNotified(version: string): boolean {
    const notifiedVersions = this.getNotifiedVersions();
    return notifiedVersions.includes(version);
  }

  /**
   * Check for new version from the backend
   */
  public async checkVersion(): Promise<VersionInfo> {
    try {
      const apiClient = await getAuthenticatedApiClient();
      const response = await apiClient.configuration_GetConfiguration();
      
      return {
        version: response.version || '0.0.0',
        environment: response.environment || 'unknown',
        useMockAuth: response.useMockAuth || false,
        timestamp: response.timestamp?.toISOString() || new Date().toISOString()
      };
    } catch (error) {
      console.error('Failed to check version:', error);
      throw new Error('Unable to check application version');
    }
  }

  /**
   * Check if there's a new version available
   */
  public async hasNewVersion(): Promise<{ hasUpdate: boolean; newVersion?: string; currentVersion?: string }> {
    try {
      const currentStoredVersion = this.getCurrentStoredVersion();
      const versionInfo = await this.checkVersion();

      if (!currentStoredVersion) {
        // First time - store current version
        this.storeVersion(versionInfo.version);
        return { hasUpdate: false };
      }

      const hasUpdate = currentStoredVersion !== versionInfo.version;
      const alreadyNotified = hasUpdate && this.isVersionNotified(versionInfo.version);
      
      return {
        hasUpdate: hasUpdate && !alreadyNotified,
        newVersion: hasUpdate && !alreadyNotified ? versionInfo.version : undefined,
        currentVersion: currentStoredVersion
      };
    } catch (error) {
      console.error('Failed to check for new version:', error);
      return { hasUpdate: false };
    }
  }

  /**
   * Start periodic version checking
   */
  public startPeriodicCheck(onNewVersionDetected: (newVersion: string, currentVersion: string) => void): void {
    // Clear any existing interval
    this.stopPeriodicCheck();

    this.checkInterval = setInterval(async () => {
      try {
        const result = await this.hasNewVersion();
        
        if (result.hasUpdate && result.newVersion && result.currentVersion) {
          console.log(`New version detected: ${result.newVersion} (current: ${result.currentVersion})`);
          // Mark this version as notified to prevent repeated notifications
          this.markVersionAsNotified(result.newVersion);
          onNewVersionDetected(result.newVersion, result.currentVersion);
        }
      } catch (error) {
        console.error('Error during periodic version check:', error);
      }
    }, VersionService.CHECK_INTERVAL);

    console.log('Started periodic version checking every', VersionService.CHECK_INTERVAL / 1000 / 60, 'minutes');
  }

  /**
   * Stop periodic version checking
   */
  public stopPeriodicCheck(): void {
    if (this.checkInterval) {
      clearInterval(this.checkInterval);
      this.checkInterval = null;
      console.log('Stopped periodic version checking');
    }
  }

  /**
   * Initialize version - store current version on first load
   */
  public async initializeVersion(): Promise<void> {
    try {
      const currentVersion = this.getCurrentStoredVersion();
      
      if (!currentVersion) {
        const versionInfo = await this.checkVersion();
        this.storeVersion(versionInfo.version);
        console.log('Initialized app version:', versionInfo.version);
      }
    } catch (error) {
      console.error('Failed to initialize version:', error);
    }
  }

  /**
   * Update to new version - clear cache and reload
   */
  public updateToNewVersion(newVersion: string): void {
    try {
      console.log('Updating to new version:', newVersion);
      
      // Store the new version
      this.storeVersion(newVersion);
      
      // Clear various caches
      this.clearCaches();
      
      // Reload the page
      window.location.reload();
    } catch (error) {
      console.error('Failed to update to new version:', error);
      // Fallback - just reload
      window.location.reload();
    }
  }

  /**
   * Clear application caches
   */
  private clearCaches(): void {
    try {
      // Clear localStorage (except version)
      const version = this.getCurrentStoredVersion();
      localStorage.clear();
      if (version) {
        localStorage.setItem(VersionService.STORAGE_KEY, version);
      }
      // Clear notified versions so future updates can be shown
      localStorage.removeItem(VersionService.NOTIFIED_VERSIONS_KEY);

      // Clear sessionStorage
      sessionStorage.clear();

      // Clear browser cache if supported
      if ('caches' in window) {
        caches.keys().then(cacheNames => {
          cacheNames.forEach(cacheName => {
            caches.delete(cacheName);
          });
        }).catch(error => {
          console.warn('Failed to clear cache storage:', error);
        });
      }

      console.log('Application caches cleared');
    } catch (error) {
      console.error('Failed to clear caches:', error);
    }
  }
}

// Export singleton instance
export const versionService = new VersionService();