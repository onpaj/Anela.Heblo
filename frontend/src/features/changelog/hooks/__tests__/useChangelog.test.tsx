/**
 * Unit tests for useChangelog hook
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import { renderHook, waitFor } from '@testing-library/react';
import { useChangelog } from '../useChangelog';
import { ChangelogData } from '../../types';

// Mock fetch
const mockFetch = jest.fn();
global.fetch = mockFetch;

const mockChangelogData: ChangelogData = {
  currentVersion: '1.2.0',
  versions: [
    {
      version: '1.2.0',
      date: '2023-01-15',
      changes: [
        {
          type: 'funkce',
          title: 'New feature',
          description: 'Description of new feature',
          source: 'commit',
          hash: 'abc123',
        },
      ],
    },
    {
      version: '1.1.0',
      date: '2023-01-01',
      changes: [
        {
          type: 'oprava',
          title: 'Bug fix',
          description: 'Fixed a bug',
          source: 'github-issue',
          id: '#123',
        },
      ],
    },
  ],
};

describe('useChangelog hook', () => {
  beforeEach(() => {
    mockFetch.mockClear();
    console.warn = jest.fn();
    console.error = jest.fn();
  });

  it('fetches changelog data successfully', async () => {
    // Mock Czech version to be successful
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve(JSON.stringify(mockChangelogData)),
    });

    const { result } = renderHook(() => useChangelog());

    expect(result.current.isLoading).toBe(true);
    expect(result.current.data).toBeNull();
    expect(result.current.error).toBeNull();

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockChangelogData);
    expect(result.current.error).toBeNull();
    expect(mockFetch).toHaveBeenCalledWith('/changelog.cs.json', {
      headers: {
        'Accept': 'application/json',
        'Cache-Control': 'no-cache',
      },
    });
  });

  it('handles 404 by creating default structure', async () => {
    // Mock Czech version to fail with 404
    mockFetch.mockRejectedValueOnce(new Error('Czech version not found: 404'));
    // Mock English version to fail with 404
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 404,
      statusText: 'Not Found',
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual({
      currentVersion: '0.1.0',
      versions: [
        {
          version: '0.1.0',
          date: expect.any(String),
          changes: [
            {
              type: 'feature',
              title: 'Systém automatického changelogu',
              description: 'Implementace automatického generování a zobrazování changelogu',
              source: 'github-issue',
              id: '#171',
            },
          ],
        },
      ],
    });
    expect(result.current.error).toBeNull();
    expect(console.warn).toHaveBeenCalledWith('Czech changelog not available, falling back to English version');
  });

  it('handles fetch errors', async () => {
    // Mock Czech version to fail with 500
    mockFetch.mockRejectedValueOnce(new Error('Czech version not found: 500'));
    // Mock English version to fail with 500
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toBeNull();
    expect(result.current.error).toBe('Failed to fetch changelog: 500 Internal Server Error');
  });

  it('handles invalid JSON', async () => {
    // Mock Czech version to return invalid JSON
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve('invalid json'),
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toBeNull();
    expect(result.current.error).toBe('Invalid JSON in changelog file');
  });

  it('handles empty response', async () => {
    // Mock Czech version to return empty response
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve(''),
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toBeNull();
    expect(result.current.error).toBe('Changelog file is empty');
  });

  it('validates changelog structure', async () => {
    const invalidData = {
      // missing currentVersion
      versions: [],
    };

    // Mock Czech version to return invalid structure
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve(JSON.stringify(invalidData)),
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toBeNull();
    expect(result.current.error).toBe('Invalid changelog data structure');
  });

  it('validates version structure', async () => {
    const invalidVersionData = {
      currentVersion: '1.0.0',
      versions: [
        {
          version: '1.0.0',
          // missing date and changes
        },
      ],
    };

    // Mock Czech version to return invalid version structure
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve(JSON.stringify(invalidVersionData)),
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toBeNull();
    expect(result.current.error).toBe('Invalid version structure: 1.0.0');
  });

  it('allows refetching data', async () => {
    // Mock Czech version for initial fetch
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve(JSON.stringify(mockChangelogData)),
    });

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toEqual(mockChangelogData);

    // Mock new data for refetch (Czech version again)
    const newData = { ...mockChangelogData, currentVersion: '1.3.0' };
    mockFetch.mockResolvedValueOnce({
      ok: true,
      text: () => Promise.resolve(JSON.stringify(newData)),
    });

    await result.current.refetch();

    await waitFor(() => {
      expect(result.current.data).toEqual(newData);
    });

    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it('handles network errors', async () => {
    // Mock Czech version to fail with network error
    mockFetch.mockRejectedValueOnce(new Error('Network error'));
    // Mock English version to also fail with network error
    mockFetch.mockRejectedValueOnce(new Error('Network error'));

    const { result } = renderHook(() => useChangelog());

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
    });

    expect(result.current.data).toBeNull();
    expect(result.current.error).toBe('Network error');
    expect(console.error).toHaveBeenCalledWith('Failed to fetch changelog:', expect.any(Error));
  });
});