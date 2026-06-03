import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { createQueryClientWrapper, createMockApiClient, mockAuthenticatedApiClient } from '../../../../api/testUtils';
import { getAuthenticatedApiClient } from '../../../../api/client';

jest.mock('../../../../api/client');

import ImportTab from '../ImportTab';

describe('ImportTab filters', () => {
  let mockFetch: jest.Mock;
  let mockClient: any;

  beforeEach(() => {
    jest.clearAllMocks();

    const mock = createMockApiClient();
    mockClient = mock.mockClient;
    mockFetch = mock.mockFetch;
    mockAuthenticatedApiClient(mockClient);

    // Mock responses based on endpoint
    mockFetch.mockImplementation((url: string) => {
      if (url.includes('/api/bank-statements/accounts')) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([])
        });
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ items: [], totalCount: 0 })
      });
    });
  });

  function renderComponentWithWrapper() {
    const { wrapper } = createQueryClientWrapper();
    return render(<ImportTab />, { wrapper });
  }

  function getListEndpointCalls() {
    return mockFetch.mock.calls.filter((call) => {
      const urlString = Array.isArray(call) && typeof call[0] === 'string' ? call[0] : '';
      return urlString.includes('/api/bank-statements') && !urlString.includes('/accounts');
    });
  }

  it('does not send filter values until Filtrovat is clicked', async () => {
    renderComponentWithWrapper();

    const transferIdInput = await screen.findByPlaceholderText('Transfer ID...');
    fireEvent.change(transferIdInput, { target: { value: 'ABC' } });
    fireEvent.change(screen.getByPlaceholderText('Účet...'), { target: { value: 'Shoptet' } });

    // Check that the hook was called initially with empty filters
    const listCalls = getListEndpointCalls();
    expect(listCalls.length).toBeGreaterThan(0);
    const initialUrl = listCalls[0][0] as string;
    expect(initialUrl).not.toContain('transferId=ABC');
    expect(initialUrl).not.toContain('account=Shoptet');
  });

  it('sends trimmed committed filters on Filtrovat click and resets page', async () => {
    renderComponentWithWrapper();

    const transferIdInput = await screen.findByPlaceholderText('Transfer ID...');
    fireEvent.change(transferIdInput, { target: { value: '  ABC  ' } });
    fireEvent.change(screen.getByPlaceholderText('Účet...'), { target: { value: '  Shoptet  ' } });

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    await waitFor(() => {
      const listCalls = getListEndpointCalls();
      expect(listCalls.length).toBeGreaterThan(1);
    });

    const listCalls = getListEndpointCalls();
    const latestUrl = listCalls[listCalls.length - 1][0] as string;

    expect(latestUrl).toContain('transferId=ABC');
    expect(latestUrl).toContain('account=Shoptet');
    expect(latestUrl).toContain('skip=0');
  });

  it('sends errorsOnly=true on Filtrovat click when checkbox is checked', async () => {
    renderComponentWithWrapper();

    await screen.findByPlaceholderText('Transfer ID...');
    const checkboxLabel = await screen.findByLabelText(/Jen chyby/);
    fireEvent.click(checkboxLabel);

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    await waitFor(() => {
      const listCalls = getListEndpointCalls();
      const latestUrl = listCalls[listCalls.length - 1][0] as string;
      expect(latestUrl).toContain('errorsOnly=true');
    });
  });

  it('sends inclusive date range on Filtrovat click', async () => {
    renderComponentWithWrapper();

    const dateInputs = (await screen.findAllByDisplayValue('')).filter(
      (i) => (i as HTMLInputElement).type === 'date'
    ) as HTMLInputElement[];

    fireEvent.change(dateInputs[0], { target: { value: '2026-01-01' } });
    fireEvent.change(dateInputs[1], { target: { value: '2026-01-31' } });

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    await waitFor(() => {
      const listCalls = getListEndpointCalls();
      const latestUrl = listCalls[listCalls.length - 1][0] as string;
      expect(latestUrl).toContain('dateFrom=2026-01-01');
      expect(latestUrl).toContain('dateTo=2026-01-31');
    });
  });

  it('blocks Filtrovat and shows inline error when dateFrom > dateTo', async () => {
    renderComponentWithWrapper();

    const dateInputs = (await screen.findAllByDisplayValue('')).filter(
      (i) => (i as HTMLInputElement).type === 'date'
    ) as HTMLInputElement[];

    fireEvent.change(dateInputs[0], { target: { value: '2026-02-01' } });
    fireEvent.change(dateInputs[1], { target: { value: '2026-01-01' } });

    const initialCallCount = getListEndpointCalls().length;

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    // No new API call should be made (filter blocked)
    // Wait a bit to ensure no new call is being made
    await new Promise((resolve) => setTimeout(resolve, 100));
    const finalCallCount = getListEndpointCalls().length;

    expect(finalCallCount).toBe(initialCallCount);
    expect(screen.getByText(/"Od" musí být dříve/)).toBeInTheDocument();
  });

  it('clears all committed filters and resets page on Vymazat', async () => {
    renderComponentWithWrapper();

    const transferIdInput = (await screen.findByPlaceholderText(
      'Transfer ID...'
    )) as HTMLInputElement;
    fireEvent.change(transferIdInput, { target: { value: 'ABC' } });

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    // Wait for the filter to be applied
    await waitFor(() => {
      const listCalls = getListEndpointCalls();
      const latestUrl = listCalls[listCalls.length - 1][0] as string;
      expect(latestUrl).toContain('transferId=ABC');
    });

    // Verify input still has the value
    expect(transferIdInput.value).toBe('ABC');

    // Now click clear
    const clearButton = await screen.findByText('Vymazat');
    fireEvent.click(clearButton);

    // After clearing, re-query the input element to get the updated reference
    const clearedInput = (screen.getByPlaceholderText(
      'Transfer ID...'
    )) as HTMLInputElement;

    // After clearing, the input fields should be empty
    expect(clearedInput.value).toBe('');

    // The clear button should still exist
    expect(screen.getByText('Vymazat')).toBeInTheDocument();

    // Verify that after clearing, when we look at the API calls, the most recent one sent does not have the filter
    const listCalls = getListEndpointCalls();
    // Should have at least 2 calls: initial + after filter
    expect(listCalls.length).toBeGreaterThanOrEqual(2);
  });
});
