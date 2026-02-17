import { renderHook } from '@testing-library/react';
import { useMediaQuery, useIsMobile } from '../useMediaQuery';

// Mock window.matchMedia
const createMatchMediaMock = (matches: boolean) => {
  return (query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: jest.fn(), // Deprecated
    removeListener: jest.fn(), // Deprecated
    addEventListener: jest.fn(),
    removeEventListener: jest.fn(),
    dispatchEvent: jest.fn(),
  });
};

describe('useMediaQuery', () => {
  beforeAll(() => {
    // Setup matchMedia mock for all tests
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: createMatchMediaMock(false),
    });
  });

  it('should return false when media query does not match', () => {
    window.matchMedia = createMatchMediaMock(false);

    const { result } = renderHook(() => useMediaQuery('(max-width: 767px)'));

    expect(result.current).toBe(false);
  });

  it('should return true when media query matches', () => {
    window.matchMedia = createMatchMediaMock(true);

    const { result } = renderHook(() => useMediaQuery('(max-width: 767px)'));

    expect(result.current).toBe(true);
  });

  it('should cleanup event listener on unmount', () => {
    const removeEventListener = jest.fn();

    window.matchMedia = (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: jest.fn(),
      removeListener: jest.fn(),
      addEventListener: jest.fn(),
      removeEventListener,
      dispatchEvent: jest.fn(),
    });

    const { unmount } = renderHook(() => useMediaQuery('(max-width: 767px)'));

    unmount();

    expect(removeEventListener).toHaveBeenCalledWith('change', expect.any(Function));
  });
});

describe('useIsMobile', () => {
  beforeAll(() => {
    // Setup matchMedia mock for all tests
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: createMatchMediaMock(false),
    });
  });

  it('should return false on desktop screens (>=768px)', () => {
    window.matchMedia = createMatchMediaMock(false);

    const { result } = renderHook(() => useIsMobile());

    expect(result.current).toBe(false);
  });

  it('should return true on mobile screens (<768px)', () => {
    window.matchMedia = createMatchMediaMock(true);

    const { result } = renderHook(() => useIsMobile());

    expect(result.current).toBe(true);
  });

  it('should use correct media query (max-width: 767px)', () => {
    const matchMediaSpy = jest.fn(createMatchMediaMock(false));
    window.matchMedia = matchMediaSpy;

    renderHook(() => useIsMobile());

    expect(matchMediaSpy).toHaveBeenCalledWith('(max-width: 767px)');
  });
});
