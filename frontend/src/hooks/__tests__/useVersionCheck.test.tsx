import { renderHook, act } from "@testing-library/react";
import { useVersionCheck } from "../useVersionCheck";
import { versionService } from "../../services/versionService";
import { useToast } from "../../contexts/ToastContext";

// Mock dependencies
jest.mock("../../services/versionService");
jest.mock("../../contexts/ToastContext");

const mockVersionService = versionService as jest.Mocked<typeof versionService>;
const mockUseToast = useToast as jest.MockedFunction<typeof useToast>;

describe("useVersionCheck", () => {
  let mockShowInfo: jest.Mock;

  beforeEach(() => {
    mockShowInfo = jest.fn();
    mockUseToast.mockReturnValue({
      showToast: jest.fn(),
      showSuccess: jest.fn(),
      showError: jest.fn(),
      showWarning: jest.fn(),
      showInfo: mockShowInfo,
    });

    // Clear all mocks
    jest.clearAllMocks();
  });

  afterEach(() => {
    // Clean up any timers
    jest.clearAllTimers();
  });

  it("should initialize version on mount when enabled", async () => {
    mockVersionService.initializeVersion.mockResolvedValue();

    const { result } = renderHook(() => useVersionCheck({ enabled: true }));

    // Wait for the hook to initialize
    await act(async () => {
      await result.current.initializeVersion();
    });

    expect(mockVersionService.initializeVersion).toHaveBeenCalled();
  });

  it("should not initialize version when disabled", async () => {
    renderHook(() => useVersionCheck({ enabled: false }));

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(mockVersionService.initializeVersion).not.toHaveBeenCalled();
  });

  it("should start periodic checking when enabled", () => {
    renderHook(() => useVersionCheck({ enabled: true }));

    expect(mockVersionService.startPeriodicCheck).toHaveBeenCalled();
  });

  it("should stop periodic checking when disabled", () => {
    const { rerender } = renderHook(
      ({ enabled }) => useVersionCheck({ enabled }),
      { initialProps: { enabled: true } },
    );

    expect(mockVersionService.startPeriodicCheck).toHaveBeenCalled();

    rerender({ enabled: false });

    expect(mockVersionService.stopPeriodicCheck).toHaveBeenCalled();
  });

  it("should show notification when new version is detected", () => {
    let onNewVersionCallback: (
      newVersion: string,
      currentVersion: string,
    ) => void;

    mockVersionService.startPeriodicCheck.mockImplementation((callback) => {
      onNewVersionCallback = callback;
    });

    renderHook(() =>
      useVersionCheck({
        enabled: true,
        showNotifications: true,
      }),
    );

    // Simulate new version detection
    act(() => {
      onNewVersionCallback("2.0.0", "1.0.0");
    });

    expect(mockShowInfo).toHaveBeenCalledWith(
      "New Version Available",
      "Version 2.0.0 is available. Click to update and refresh the application.",
      expect.objectContaining({
        duration: 0,
        action: expect.objectContaining({
          label: "Update Now",
          onClick: expect.any(Function),
        }),
      }),
    );
  });

  it("should not show notification when showNotifications is false", () => {
    let onNewVersionCallback: (
      newVersion: string,
      currentVersion: string,
    ) => void;

    mockVersionService.startPeriodicCheck.mockImplementation((callback) => {
      onNewVersionCallback = callback;
    });

    renderHook(() =>
      useVersionCheck({
        enabled: true,
        showNotifications: false,
      }),
    );

    act(() => {
      onNewVersionCallback("2.0.0", "1.0.0");
    });

    expect(mockShowInfo).not.toHaveBeenCalled();
  });

  it("should call custom callback when provided", () => {
    const customCallback = jest.fn();
    let onNewVersionCallback: (
      newVersion: string,
      currentVersion: string,
    ) => void;

    mockVersionService.startPeriodicCheck.mockImplementation((callback) => {
      onNewVersionCallback = callback;
    });

    renderHook(() =>
      useVersionCheck({
        enabled: true,
        onNewVersionDetected: customCallback,
      }),
    );

    act(() => {
      onNewVersionCallback("2.0.0", "1.0.0");
    });

    expect(customCallback).toHaveBeenCalledWith("2.0.0", "1.0.0");
  });

  it("should provide checkVersion function", async () => {
    mockVersionService.hasNewVersion.mockResolvedValue({
      hasUpdate: false,
    });

    const { result } = renderHook(() => useVersionCheck());

    await act(async () => {
      await result.current.checkVersion();
    });

    expect(mockVersionService.hasNewVersion).toHaveBeenCalled();
  });

  it("should provide initializeVersion function", async () => {
    mockVersionService.initializeVersion.mockResolvedValue();

    const { result } = renderHook(() => useVersionCheck());

    await act(async () => {
      await result.current.initializeVersion();
    });

    expect(mockVersionService.initializeVersion).toHaveBeenCalled();
  });

  it("should provide updateToNewVersion function", () => {
    const { result } = renderHook(() => useVersionCheck());

    act(() => {
      result.current.updateToNewVersion("2.0.0");
    });

    expect(mockVersionService.updateToNewVersion).toHaveBeenCalledWith("2.0.0");
  });

  it("should provide getCurrentVersion function", () => {
    mockVersionService.getCurrentStoredVersion.mockReturnValue("1.0.0");

    const { result } = renderHook(() => useVersionCheck());

    const version = result.current.getCurrentVersion();

    expect(version).toBe("1.0.0");
    expect(mockVersionService.getCurrentStoredVersion).toHaveBeenCalled();
  });

  it("should clean up on unmount", () => {
    const { unmount } = renderHook(() => useVersionCheck({ enabled: true }));

    unmount();

    expect(mockVersionService.stopPeriodicCheck).toHaveBeenCalled();
  });

  it("should handle version check errors gracefully", async () => {
    const consoleSpy = jest
      .spyOn(console, "error")
      .mockImplementation(() => {});
    mockVersionService.hasNewVersion.mockRejectedValue(
      new Error("Check failed"),
    );

    const { result } = renderHook(() => useVersionCheck());

    await act(async () => {
      await result.current.checkVersion();
    });

    expect(consoleSpy).toHaveBeenCalledWith(
      "Manual version check failed:",
      expect.any(Error),
    );

    consoleSpy.mockRestore();
  });

  it("should handle initialization errors gracefully", async () => {
    const consoleSpy = jest
      .spyOn(console, "error")
      .mockImplementation(() => {});
    mockVersionService.initializeVersion.mockRejectedValue(
      new Error("Init failed"),
    );

    const { result } = renderHook(() => useVersionCheck());

    await act(async () => {
      await result.current.initializeVersion();
    });

    expect(consoleSpy).toHaveBeenCalledWith(
      "Version initialization failed:",
      expect.any(Error),
    );

    consoleSpy.mockRestore();
  });
});
