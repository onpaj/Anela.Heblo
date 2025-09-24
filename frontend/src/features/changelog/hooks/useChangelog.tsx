/**
 * Changelog data fetching hook
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import { useState, useEffect, useCallback } from 'react';
import { ChangelogData, UseChangelogReturn, ChangelogFetchError, ChangelogParseError } from '../types';

/**
 * Hook for fetching and managing changelog data
 */
export function useChangelog(): UseChangelogReturn {
  const [data, setData] = useState<ChangelogData | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  /**
   * Fetch changelog data from the embedded JSON file
   * First tries Czech version (changelog.cs.json), then falls back to English (changelog.json)
   */
  const fetchChangelog = useCallback(async (): Promise<void> => {
    try {
      setIsLoading(true);
      setError(null);

      let response: Response;
      let usedUrl = '';

      // Try Czech version first
      try {
        usedUrl = '/changelog.cs.json';
        response = await fetch(usedUrl, {
          headers: {
            'Accept': 'application/json',
            'Cache-Control': 'no-cache',
          },
        });
        
        if (!response.ok) {
          throw new Error(`Czech version not found: ${response.status}`);
        }
        
        console.log('Loading Czech changelog version');
      } catch (czechError) {
        // Fallback to English version
        console.warn('Czech changelog not available, falling back to English version');
        
        usedUrl = '/changelog.json';
        response = await fetch(usedUrl, {
          headers: {
            'Accept': 'application/json',
            'Cache-Control': 'no-cache',
          },
        });

        if (!response.ok) {
          // If both files don't exist, create a default structure
          if (response.status === 404) {
            console.warn('No changelog files found, using default structure');
            
            const defaultData: ChangelogData = {
              currentVersion: '0.1.0',
              versions: [
                {
                  version: '0.1.0',
                  date: new Date().toISOString().split('T')[0], // Today's date
                  changes: [
                    {
                      type: 'feature',
                      title: 'Systém automatického changelogu',
                      description: 'Implementace automatického generování a zobrazování changelogu',
                      source: 'github-issue',
                      id: '#171'
                    }
                  ]
                }
              ]
            };
            
            setData(defaultData);
            setIsLoading(false);
            return;
          }

          throw new ChangelogFetchError(
            `Failed to fetch changelog: ${response.status} ${response.statusText}`
          );
        }
        
        console.log('Loading English changelog version');
      }

      const rawData = await response.text();
      
      if (!rawData.trim()) {
        throw new ChangelogParseError('Changelog file is empty');
      }

      let parsedData: ChangelogData;
      try {
        parsedData = JSON.parse(rawData);
      } catch (parseError) {
        throw new ChangelogParseError(
          'Invalid JSON in changelog file',
          parseError instanceof Error ? parseError : undefined
        );
      }

      // Validate the structure
      if (!parsedData.currentVersion || !Array.isArray(parsedData.versions)) {
        throw new ChangelogParseError('Invalid changelog data structure');
      }

      // Validate each version
      for (const version of parsedData.versions) {
        if (!version.version || !version.date || !Array.isArray(version.changes)) {
          throw new ChangelogParseError(`Invalid version structure: ${version.version}`);
        }
      }

      setData(parsedData);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Unknown error occurred';
      console.error('Failed to fetch changelog:', err);
      setError(errorMessage);
    } finally {
      setIsLoading(false);
    }
  }, []);

  /**
   * Refetch changelog data
   */
  const refetch = useCallback(async (): Promise<void> => {
    await fetchChangelog();
  }, [fetchChangelog]);

  // Initial fetch on mount
  useEffect(() => {
    fetchChangelog();
  }, [fetchChangelog]);

  return {
    data,
    isLoading,
    error,
    refetch,
  };
}