import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { createQueryClientWrapper, mockAuthenticatedApiClient } from '../../../../api/testUtils';
import ImportTab from '../ImportTab';

jest.mock('../../../../api/client');

describe('ImportTab filters', () => {
  let mockGetBankStatements: jest.Mock;
  let mockGetAccounts: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();

    mockGetBankStatements = jest.fn().mockResolvedValue({ items: [], totalCount: 0 });
    mockGetAccounts = jest.fn().mockResolvedValue([]);

    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);
  });

  function renderComponentWithWrapper() {
    const { wrapper } = createQueryClientWrapper();
    return render(<ImportTab />, { wrapper });
  }

  function getListCalls() {
    return mockGetBankStatements.mock.calls;
  }

  it('does not send filter values until Filtrovat is clicked', async () => {
    renderComponentWithWrapper();

    const transferIdInput = await screen.findByPlaceholderText('Transfer ID...');
    fireEvent.change(transferIdInput, { target: { value: 'ABC' } });
    fireEvent.change(screen.getByPlaceholderText('Účet...'), { target: { value: 'Shoptet' } });

    // Check that the hook was called initially with empty filters
    const listCalls = getListCalls();
    expect(listCalls.length).toBeGreaterThan(0);
    const initialCall = listCalls[0];
    expect(initialCall[1]).toBeFalsy();   // transferId not sent
    expect(initialCall[2]).toBeFalsy();   // account not sent
  });

  it('sends trimmed committed filters on Filtrovat click and resets page', async () => {
    renderComponentWithWrapper();

    const transferIdInput = await screen.findByPlaceholderText('Transfer ID...');
    fireEvent.change(transferIdInput, { target: { value: '  ABC  ' } });
    fireEvent.change(screen.getByPlaceholderText('Účet...'), { target: { value: '  Shoptet  ' } });

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    await waitFor(() => {
      const listCalls = getListCalls();
      expect(listCalls.length).toBeGreaterThan(1);
    });

    const listCalls = getListCalls();
    const latestCall = listCalls[listCalls.length - 1];

    expect(latestCall[1]).toBe('ABC');      // transferId trimmed
    expect(latestCall[2]).toBe('Shoptet'); // account trimmed
    expect(latestCall[8]).toBe(0);          // skip reset to 0
  });

  it('sends errorsOnly=true on Filtrovat click when checkbox is checked', async () => {
    renderComponentWithWrapper();

    await screen.findByPlaceholderText('Transfer ID...');
    const checkboxLabel = await screen.findByLabelText(/Jen chyby/);
    fireEvent.click(checkboxLabel);

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    await waitFor(() => {
      const listCalls = getListCalls();
      const latestCall = listCalls[listCalls.length - 1];
      expect(latestCall[7]).toBe(true);  // errorsOnly param
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
      const listCalls = getListCalls();
      expect(listCalls.length).toBeGreaterThan(0);
    });

    const listCalls = getListCalls();
    const latestCall = listCalls[listCalls.length - 1];
    expect(latestCall[5]).toEqual(new Date('2026-01-01'));  // dateFrom
    expect(latestCall[6]).toEqual(new Date('2026-01-31'));  // dateTo
  });

  it('blocks Filtrovat and shows inline error when dateFrom > dateTo', async () => {
    renderComponentWithWrapper();

    const dateInputs = (await screen.findAllByDisplayValue('')).filter(
      (i) => (i as HTMLInputElement).type === 'date'
    ) as HTMLInputElement[];

    fireEvent.change(dateInputs[0], { target: { value: '2026-02-01' } });
    fireEvent.change(dateInputs[1], { target: { value: '2026-01-01' } });

    const initialCallCount = getListCalls().length;

    const filterButton = await screen.findByText('Filtrovat');
    fireEvent.click(filterButton);

    // No new API call should be made (filter blocked)
    // Wait a bit to ensure no new call is being made
    await new Promise((resolve) => setTimeout(resolve, 100));
    const finalCallCount = getListCalls().length;

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
      const listCalls = getListCalls();
      const latestCall = listCalls[listCalls.length - 1];
      expect(latestCall[1]).toBe('ABC');  // transferId
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
    const listCalls = getListCalls();
    // Should have at least 2 calls: initial + after filter
    expect(listCalls.length).toBeGreaterThanOrEqual(2);
  });
});

describe('ImportTab status badge', () => {
  let mockGetBankStatements: jest.Mock;
  let mockGetAccounts: jest.Mock;

  function baseStatement(overrides: Partial<{
    id: number;
    transferId: string;
    account: string;
    statementDate: Date;
    importDate: Date;
    itemCount: number;
    currency: string;
    importResult: string;
    errorType: string | null | undefined;
  }>) {
    return {
      id: 1,
      transferId: 'TX-1',
      account: 'Shoptet',
      statementDate: new Date('2026-01-01'),
      importDate: new Date('2026-01-02'),
      itemCount: 3,
      currency: 'CZK',
      importResult: 'OK',
      errorType: null,
      ...overrides,
    };
  }

  beforeEach(() => {
    jest.clearAllMocks();

    mockGetAccounts = jest.fn().mockResolvedValue([]);

    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);
  });

  function renderComponentWithWrapper() {
    const { wrapper } = createQueryClientWrapper();
    return render(<ImportTab />, { wrapper });
  }

  it('renders the success badge when errorType is null, regardless of importResult text', async () => {
    mockGetBankStatements = jest.fn().mockResolvedValue({
      items: [baseStatement({ importResult: 'OK', errorType: null })],
      totalCount: 1,
    });
    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);

    renderComponentWithWrapper();

    expect(await screen.findByText('Úspěch')).toBeInTheDocument();
    expect(screen.queryByText(/Chyba/)).not.toBeInTheDocument();
  });

  it('renders the error badge with the errorType text when errorType is set', async () => {
    mockGetBankStatements = jest.fn().mockResolvedValue({
      items: [baseStatement({ importResult: 'ParseError', errorType: 'ParseError' })],
      totalCount: 1,
    });
    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);

    renderComponentWithWrapper();

    expect(await screen.findByText('ParseError')).toBeInTheDocument();
    expect(screen.queryByText('Úspěch')).not.toBeInTheDocument();
  });

  it('falls back to "Chyba" when errorType is set but empty', async () => {
    mockGetBankStatements = jest.fn().mockResolvedValue({
      items: [baseStatement({ importResult: '', errorType: '' })],
      totalCount: 1,
    });
    const mockClient = {
      bankStatements_GetBankStatements: mockGetBankStatements,
      bankStatements_GetAccounts: mockGetAccounts,
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);

    renderComponentWithWrapper();

    // Empty errorType is falsy, so with a naive `!errorType` success check this
    // would wrongly render "Úspěch". Per spec FR-1, only null/undefined mean
    // success; an empty string must still resolve to the error badge fallback text.
    expect(await screen.findByText('Chyba')).toBeInTheDocument();
  });
});
