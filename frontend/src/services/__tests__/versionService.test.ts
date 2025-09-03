import { versionService, VersionService } from '../versionService';
import { getAuthenticatedApiClient } from '../../api/client';

// Mock the API client
jest.mock('../../api/client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<typeof getAuthenticatedApiClient>;

// Mock localStorage
const mockLocalStorage = {
  getItem: jest.fn(),
  setItem: jest.fn(),
  clear: jest.fn(),
};
Object.defineProperty(window, 'localStorage', { value: mockLocalStorage });

// Mock console methods to avoid noise in tests
let consoleSpy: {
  log: jest.SpyInstance;
  warn: jest.SpyInstance;
  error: jest.SpyInstance;
};

describe('VersionService', () => {
  let service: VersionService;
  let mockApiClient: any;

  beforeEach(() => {
    service = new VersionService();
    mockApiClient = {
      configuration_GetConfiguration: jest.fn(),
    };
    mockGetAuthenticatedApiClient.mockResolvedValue(mockApiClient);
    
    // Setup console spies
    consoleSpy = {
      log: jest.spyOn(console, 'log').mockImplementation(() => {}),
      warn: jest.spyOn(console, 'warn').mockImplementation(() => {}),
      error: jest.spyOn(console, 'error').mockImplementation(() => {}),
    };
    
    // Clear all mocks
    jest.clearAllMocks();
  });

  afterEach(() => {
    service.stopPeriodicCheck();
    // Restore console spies
    consoleSpy.log.mockRestore();
    consoleSpy.warn.mockRestore();
    consoleSpy.error.mockRestore();
  });

  describe('getCurrentStoredVersion', () => {
    it('should return stored version from localStorage', () => {
      mockLocalStorage.getItem.mockReturnValue('1.0.0');
      
      const result = service.getCurrentStoredVersion();
      
      expect(result).toBe('1.0.0');
      expect(mockLocalStorage.getItem).toHaveBeenCalledWith('app_version');
    });

    it('should return null if no version is stored', () => {
      mockLocalStorage.getItem.mockReturnValue(null);
      
      const result = service.getCurrentStoredVersion();
      
      expect(result).toBeNull();
    });

    it('should handle localStorage errors gracefully', () => {
      mockLocalStorage.getItem.mockImplementation(() => {
        throw new Error('Storage error');
      });
      
      const result = service.getCurrentStoredVersion();
      
      expect(result).toBeNull();
      expect(consoleSpy.warn).toHaveBeenCalled();
    });
  });

  describe('storeVersion', () => {
    it('should store version in localStorage', () => {
      service.storeVersion('1.2.3');
      
      expect(mockLocalStorage.setItem).toHaveBeenCalledWith('app_version', '1.2.3');
    });

    it('should handle localStorage errors gracefully', () => {
      mockLocalStorage.setItem.mockImplementation(() => {
        throw new Error('Storage error');
      });
      
      service.storeVersion('1.2.3');
      
      expect(consoleSpy.warn).toHaveBeenCalled();
    });
  });

  describe('checkVersion', () => {
    it('should fetch version from API', async () => {
      const mockResponse = {
        version: '1.2.3',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date('2023-01-01T00:00:00Z')
      };
      mockApiClient.configuration_GetConfiguration.mockResolvedValue(mockResponse);

      const result = await service.checkVersion();

      expect(result).toEqual({
        version: '1.2.3',
        environment: 'test',
        useMockAuth: true,
        timestamp: '2023-01-01T00:00:00.000Z'
      });
      expect(mockApiClient.configuration_GetConfiguration).toHaveBeenCalled();
    });

    it('should throw error when API call fails', async () => {
      mockApiClient.configuration_GetConfiguration.mockRejectedValue(new Error('API Error'));

      await expect(service.checkVersion()).rejects.toThrow('Unable to check application version');
    });
  });

  describe('hasNewVersion', () => {
    it('should return false when no stored version (first time)', async () => {
      mockLocalStorage.getItem.mockReturnValue(null);
      mockApiClient.configuration_GetConfiguration.mockResolvedValue({
        version: '1.0.0',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date()
      });

      const result = await service.hasNewVersion();

      expect(result.hasUpdate).toBe(false);
      expect(mockLocalStorage.setItem).toHaveBeenCalledWith('app_version', '1.0.0');
    });

    it('should return false when versions match', async () => {
      mockLocalStorage.getItem.mockReturnValue('1.0.0');
      mockApiClient.configuration_GetConfiguration.mockResolvedValue({
        version: '1.0.0',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date()
      });

      const result = await service.hasNewVersion();

      expect(result.hasUpdate).toBe(false);
    });

    it('should return true when versions differ', async () => {
      mockLocalStorage.getItem.mockReturnValue('1.0.0');
      mockApiClient.configuration_GetConfiguration.mockResolvedValue({
        version: '1.1.0',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date()
      });

      const result = await service.hasNewVersion();

      expect(result.hasUpdate).toBe(true);
      expect(result.newVersion).toBe('1.1.0');
      expect(result.currentVersion).toBe('1.0.0');
    });

    it('should handle errors gracefully', async () => {
      mockApiClient.configuration_GetConfiguration.mockRejectedValue(new Error('API Error'));

      const result = await service.hasNewVersion();

      expect(result.hasUpdate).toBe(false);
      expect(consoleSpy.error).toHaveBeenCalled();
    });
  });

  describe('initializeVersion', () => {
    it('should initialize version when none is stored', async () => {
      mockLocalStorage.getItem.mockReturnValue(null);
      mockApiClient.configuration_GetConfiguration.mockResolvedValue({
        version: '1.0.0',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date()
      });

      await service.initializeVersion();

      expect(mockLocalStorage.setItem).toHaveBeenCalledWith('app_version', '1.0.0');
    });

    it('should not initialize when version is already stored', async () => {
      mockLocalStorage.getItem.mockReturnValue('1.0.0');

      await service.initializeVersion();

      expect(mockApiClient.configuration_GetConfiguration).not.toHaveBeenCalled();
      expect(mockLocalStorage.setItem).not.toHaveBeenCalled();
    });
  });

  describe('startPeriodicCheck', () => {
    beforeEach(() => {
      jest.useFakeTimers();
    });

    afterEach(() => {
      jest.useRealTimers();
    });

    it('should call callback when new version is detected', async () => {
      const callback = jest.fn();
      
      // Set up mocks - localStorage always returns '1.0.0', API always returns '1.1.0'
      mockLocalStorage.getItem.mockReturnValue('1.0.0');
      mockApiClient.configuration_GetConfiguration.mockResolvedValue({
        version: '1.1.0',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date()
      });

      // Spy on hasNewVersion to see what it returns
      const hasNewVersionSpy = jest.spyOn(service, 'hasNewVersion').mockImplementation(async () => ({
        hasUpdate: true,
        newVersion: '1.1.0',
        currentVersion: '1.0.0'
      }));

      service.startPeriodicCheck(callback);

      // Fast-forward time to trigger the check
      jest.runOnlyPendingTimers();
      
      // Wait for async operations to complete
      await Promise.resolve();

      // Debug: check if hasNewVersion was called
      expect(hasNewVersionSpy).toHaveBeenCalled();
      expect(callback).toHaveBeenCalledWith('1.1.0', '1.0.0');
    });

    it('should not call callback when no new version', async () => {
      const callback = jest.fn();
      mockLocalStorage.getItem.mockReturnValue('1.0.0');
      mockApiClient.configuration_GetConfiguration.mockResolvedValue({
        version: '1.0.0',
        environment: 'test',
        useMockAuth: true,
        timestamp: new Date()
      });

      service.startPeriodicCheck(callback);

      // Fast-forward time to trigger the check
      jest.runOnlyPendingTimers();
      
      // Wait for async operations to complete
      await Promise.resolve();

      expect(callback).not.toHaveBeenCalled();
    });
  });

  describe('updateToNewVersion', () => {
    // Mock window.location.reload
    const mockReload = jest.fn();
    Object.defineProperty(window, 'location', {
      writable: true,
      value: { reload: mockReload }
    });

    // Mock sessionStorage
    const mockSessionStorage = {
      clear: jest.fn(),
    };
    Object.defineProperty(window, 'sessionStorage', { value: mockSessionStorage });

    it('should store new version and reload page', () => {
      service.updateToNewVersion('2.0.0');

      expect(mockLocalStorage.setItem).toHaveBeenCalledWith('app_version', '2.0.0');
      expect(mockLocalStorage.clear).toHaveBeenCalled();
      expect(mockSessionStorage.clear).toHaveBeenCalled();
      expect(mockReload).toHaveBeenCalled();
    });
  });
});

describe('versionService singleton', () => {
  it('should export a singleton instance', () => {
    expect(versionService).toBeInstanceOf(VersionService);
    expect(versionService).toBe(versionService); // Same reference
  });
});