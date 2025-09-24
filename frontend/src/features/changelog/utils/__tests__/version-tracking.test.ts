/**
 * Unit tests for version tracking utility
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import {
  compareVersions,
  getVersionTracking,
  saveVersionTracking,
  isNewVersion,
  hasSeenVersion,
  markVersionAsSeen,
  resetVersionTracking,
  getLastShownVersion,
  getSeenVersions,
  shouldShowToaster,
  migrateVersionTracking,
} from '../version-tracking';
import { VersionTracking } from '../../types';

// Mock localStorage
const localStorageMock = (() => {
  let store: Record<string, string> = {};

  const mockStorage = {
    getItem: jest.fn(),
    setItem: jest.fn(),
    removeItem: jest.fn(),
    clear: jest.fn(),
    // Helper to access internal store for debugging
    _getStore: () => store,
  };

  // Setup implementations
  mockStorage.getItem.mockImplementation((key: string) => {
    return store[key] || null;
  });

  mockStorage.setItem.mockImplementation((key: string, value: string) => {
    store[key] = value;
  });

  mockStorage.removeItem.mockImplementation((key: string) => {
    delete store[key];
  });

  mockStorage.clear.mockImplementation(() => {
    store = {};
  });

  return mockStorage;
})();

Object.defineProperty(window, 'localStorage', {
  value: localStorageMock,
});

describe('version-tracking utility', () => {
  beforeEach(() => {
    // Clear data but preserve mock implementations
    localStorageMock.clear();
    
    // Clear mock call history but restore implementations
    localStorageMock.getItem.mockClear();
    localStorageMock.setItem.mockClear();
    localStorageMock.removeItem.mockClear();
  });

  describe('compareVersions', () => {
    it('should correctly compare semantic versions', () => {
      expect(compareVersions('1.2.3', '1.2.2')).toEqual({
        isNewer: true,
        comparison: 1,
      });

      expect(compareVersions('1.2.2', '1.2.3')).toEqual({
        isNewer: false,
        comparison: -1,
      });

      expect(compareVersions('1.2.3', '1.2.3')).toEqual({
        isNewer: false,
        comparison: 0,
      });
    });

    it('should handle versions with v prefix', () => {
      expect(compareVersions('v1.2.3', 'v1.2.2')).toEqual({
        isNewer: true,
        comparison: 1,
      });
    });

    it('should handle major version differences', () => {
      expect(compareVersions('2.0.0', '1.9.9')).toEqual({
        isNewer: true,
        comparison: 1,
      });
    });

    it('should handle minor version differences', () => {
      expect(compareVersions('1.3.0', '1.2.9')).toEqual({
        isNewer: true,
        comparison: 1,
      });
    });

    it('should handle missing version parts', () => {
      expect(compareVersions('1.2', '1.1.9')).toEqual({
        isNewer: true,
        comparison: 1,
      });
    });
  });

  describe('getVersionTracking', () => {
    it('should return default tracking when no data exists', () => {
      const tracking = getVersionTracking();
      
      expect(tracking.lastShownVersion).toBe('0.0.0');
      expect(tracking.seenVersions).toEqual([]);
      expect(tracking.lastShownAt).toBeDefined();
    });

    it('should return stored tracking data', () => {
      const mockTracking: VersionTracking = {
        lastShownVersion: '1.2.3',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0', '1.1.0', '1.2.3'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(mockTracking));

      const tracking = getVersionTracking();
      expect(tracking).toEqual(mockTracking);
    });

    it('should handle corrupted data gracefully', () => {
      localStorageMock.getItem.mockReturnValue('invalid-json');

      const tracking = getVersionTracking();
      expect(tracking.lastShownVersion).toBe('0.0.0');
    });

    it('should validate data structure', () => {
      const invalidTracking = {
        lastShownVersion: '1.2.3',
        // missing lastShownAt and seenVersions
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(invalidTracking));

      const tracking = getVersionTracking();
      expect(tracking.lastShownVersion).toBe('0.0.0');
    });
  });

  describe('saveVersionTracking', () => {
    it('should save tracking data to localStorage', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.3',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0', '1.1.0', '1.2.3'],
      };

      saveVersionTracking(tracking);

      expect(localStorageMock.setItem).toHaveBeenCalledWith(
        'anela-heblo-version-tracking',
        JSON.stringify(tracking)
      );
    });
  });

  describe('isNewVersion', () => {
    it('should return true for newer versions', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.2.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(isNewVersion('1.2.1')).toBe(true);
      expect(isNewVersion('1.3.0')).toBe(true);
      expect(isNewVersion('2.0.0')).toBe(true);
    });

    it('should return false for same or older versions', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.2.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(isNewVersion('1.2.0')).toBe(false);
      expect(isNewVersion('1.1.0')).toBe(false);
      expect(isNewVersion('0.9.0')).toBe(false);
    });
  });

  describe('hasSeenVersion', () => {
    it('should return true if version was seen', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0', '1.1.0', '1.2.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(hasSeenVersion('1.1.0')).toBe(true);
      expect(hasSeenVersion('1.2.0')).toBe(true);
    });

    it('should return false if version was not seen', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0', '1.1.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(hasSeenVersion('1.3.0')).toBe(false);
      expect(hasSeenVersion('2.0.0')).toBe(false);
    });
  });

  describe('markVersionAsSeen', () => {
    it('should update last shown version for newer version', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.2.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      markVersionAsSeen('1.3.0');

      const setItemCalls = localStorageMock.setItem.mock.calls;
      const lastCall = setItemCalls[setItemCalls.length - 1];
      const savedData = JSON.parse(lastCall[1]);
      expect(savedData.lastShownVersion).toBe('1.3.0');
      expect(savedData.seenVersions).toContain('1.3.0');
    });

    it('should not update last shown version for older version', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.2.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      markVersionAsSeen('1.1.0');

      const setItemCalls = localStorageMock.setItem.mock.calls;
      const lastCall = setItemCalls[setItemCalls.length - 1];
      const savedData = JSON.parse(lastCall[1]);
      expect(savedData.lastShownVersion).toBe('1.2.0');
      expect(savedData.seenVersions).toContain('1.1.0');
    });

    it('should limit seen versions to 10', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: Array.from({ length: 10 }, (_, i) => `1.${i}.0`),
      };

      const jsonString = JSON.stringify(tracking);
      
      // Directly mock getItem to return our test data instead of relying on setItem
      localStorageMock.getItem.mockReturnValue(jsonString);

      markVersionAsSeen('2.0.0');

      // Get the last setItem call (which should be from markVersionAsSeen)
      const setItemCalls = localStorageMock.setItem.mock.calls;
      const lastCall = setItemCalls[setItemCalls.length - 1];
      const savedData = JSON.parse(lastCall[1]);
      
      expect(savedData.seenVersions).toHaveLength(10);
      expect(savedData.seenVersions).toContain('2.0.0');
      expect(savedData.seenVersions).not.toContain('1.0.0'); // First one should be removed
    });
  });

  describe('shouldShowToaster', () => {
    it('should return false for development versions', () => {
      expect(shouldShowToaster('0.0.0')).toBe(false);
      expect(shouldShowToaster('1.0.0-dev')).toBe(false);
      expect(shouldShowToaster('1.0.0-local')).toBe(false);
    });

    it('should return true for new production versions', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.0.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(shouldShowToaster('1.1.0')).toBe(true);
    });

    it('should return false for already seen versions', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.1.0',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0', '1.1.0'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(shouldShowToaster('1.1.0')).toBe(false);
    });
  });

  describe('resetVersionTracking', () => {
    it('should remove tracking data from localStorage', () => {
      resetVersionTracking();
      expect(localStorageMock.removeItem).toHaveBeenCalledWith('anela-heblo-version-tracking');
    });
  });

  describe('getLastShownVersion', () => {
    it('should return last shown version', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.3',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.2.3'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(getLastShownVersion()).toBe('1.2.3');
    });

    it('should return 0.0.0 on error', () => {
      localStorageMock.getItem.mockReturnValue('invalid-json');

      expect(getLastShownVersion()).toBe('0.0.0');
    });
  });

  describe('getSeenVersions', () => {
    it('should return seen versions array', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.3',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.0.0', '1.1.0', '1.2.3'],
      };

      localStorageMock.getItem.mockReturnValue(JSON.stringify(tracking));

      expect(getSeenVersions()).toEqual(['1.0.0', '1.1.0', '1.2.3']);
    });

    it('should return empty array on error', () => {
      localStorageMock.getItem.mockReturnValue('invalid-json');

      expect(getSeenVersions()).toEqual([]);
    });
  });

  describe('migrateVersionTracking', () => {
    it('should migrate from old localStorage keys', () => {
      // Setup: no current data, but old data exists
      localStorageMock.getItem.mockImplementation((key: string) => {
        if (key === 'anela-heblo-version-tracking') return null;
        if (key === 'changelog-version') return '1.0.0';
        return null;
      });

      migrateVersionTracking();

      // Check that migration happened
      const setItemCalls = localStorageMock.setItem.mock.calls;
      const migrationCall = setItemCalls.find(call => 
        call[0] === 'anela-heblo-version-tracking' && 
        call[1].includes('"lastShownVersion":"1.0.0"')
      );
      
      expect(migrationCall).toBeDefined();
      expect(localStorageMock.removeItem).toHaveBeenCalledWith('changelog-version');
    });

    it('should not migrate if current data exists', () => {
      const tracking: VersionTracking = {
        lastShownVersion: '1.2.3',
        lastShownAt: '2023-01-01T00:00:00.000Z',
        seenVersions: ['1.2.3'],
      };

      localStorageMock.getItem.mockImplementation((key: string) => {
        if (key === 'anela-heblo-version-tracking') return JSON.stringify(tracking);
        if (key === 'changelog-version') return '1.0.0';
        return null;
      });

      migrateVersionTracking();

      expect(localStorageMock.removeItem).not.toHaveBeenCalledWith('changelog-version');
    });
  });
});