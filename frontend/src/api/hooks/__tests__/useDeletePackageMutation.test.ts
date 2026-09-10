import React, { ReactNode } from 'react';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useDeletePackageMutation } from '../usePackages';
import { getAuthenticatedApiClient } from '../../client';
import { SwaggerException } from '../../generated/api-client';

jest.mock('../../client', () => ({
  ...jest.requireActual('../../client'),
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

describe('useDeletePackageMutation', () => {
  let queryClient: QueryClient;
  let mockPackaging_DeletePackage: jest.Mock;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    mockPackaging_DeletePackage = jest.fn();
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue({
      packaging_DeletePackage: mockPackaging_DeletePackage,
    } as any);
  });

  const wrapper = ({ children }: { children: ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);

  it('deletes the package by id', async () => {
    // Arrange
    mockPackaging_DeletePackage.mockResolvedValue({ success: true, deleted: true });

    // Act
    const { result } = renderHook(() => useDeletePackageMutation(), { wrapper });
    result.current.mutate(42);

    // Assert
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockPackaging_DeletePackage).toHaveBeenCalledWith(42);
  });

  // PackageNotFound maps to HTTP 404, so the generated client rejects rather than resolving
  // — the envelope has to be read off the exception for the translated message to survive.
  it('throws the translated message when the client rejects with a non-200 carrying the envelope', async () => {
    // Arrange
    mockPackaging_DeletePackage.mockRejectedValue(
      new SwaggerException(
        'An unexpected server error occurred.',
        404,
        JSON.stringify({ success: false, errorCode: 'PackageNotFound' }),
        {},
        null,
      ),
    );

    // Act
    const { result } = renderHook(() => useDeletePackageMutation(), { wrapper });
    result.current.mutate(42);

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Zásilka nebyla nalezena.');
  });

  it('throws a generic message when the rejection carries no error envelope', async () => {
    // Arrange
    mockPackaging_DeletePackage.mockRejectedValue(new TypeError('Failed to fetch'));

    // Act
    const { result } = renderHook(() => useDeletePackageMutation(), { wrapper });
    result.current.mutate(42);

    // Assert
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toBe('Nepodařilo se smazat balík.');
  });
});
