import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import TransportBoxCheck from '../TransportBoxCheck';
import { useTransportBoxByCodeQuery } from '../../../api/hooks/useTransportBoxes';

jest.mock('../../../api/hooks/useTransportBoxes', () => ({
  useTransportBoxByCodeQuery: jest.fn(),
}));

const mockHook = useTransportBoxByCodeQuery as jest.Mock;

const fakeBox = {
  id: 1,
  code: 'B001',
  state: 'InTransit',
  location: 'Kumbal',
  description: 'Testovací box',
  items: [
    {
      id: 10,
      productCode: 'MED001',
      productName: 'Obvazy',
      amount: 5,
      lotNumber: 'LOT-2026',
      expirationDate: new Date('2027-01-01'),
    },
  ],
  stateLog: [
    { id: 1, state: 'New', stateDate: new Date('2026-05-01T08:00:00'), user: 'system' },
    { id: 2, state: 'InTransit', stateDate: new Date('2026-05-10T09:00:00'), user: 'jan' },
  ],
};

beforeEach(() => {
  jest.useFakeTimers();
  mockHook.mockReset();
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

const scan = (code: string) => {
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: code } });
  fireEvent.submit(input.form!);
};

describe('TransportBoxCheck', () => {
  it('renders a focused scan input on mount', () => {
    mockHook.mockReturnValue({ data: undefined, isFetching: false, isError: false });
    render(<TransportBoxCheck />);
    expect(screen.getByRole('textbox')).toHaveFocus();
  });

  it('shows box detail with contents and history after a resolving scan', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B001'
        ? { data: fakeBox, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('b001'));

    expect(screen.getByText('B001')).toBeInTheDocument();
    expect(screen.getByText('Testovací box')).toBeInTheDocument();
    // Contents tab is active by default
    expect(screen.getByText('Obvazy')).toBeInTheDocument();

    // Switch to history tab
    fireEvent.click(screen.getByTestId('tab-history'));
    expect(screen.getByText('jan')).toBeInTheDocument();
    expect(screen.queryByText('Obvazy')).not.toBeInTheDocument();
  });

  it('shows TerminalError with the error message for an unknown code', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: true, error: new Error('Box nenalezen') }
        : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('B999'));

    expect(screen.getByTestId('terminal-error')).toBeInTheDocument();
    expect(screen.getByText('Box nenalezen')).toBeInTheDocument();
    expect(screen.getByText('Zkontrolujte kód a naskenujte znovu')).toBeInTheDocument();
  });

  it('shows Czech fallback for a network error', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: true, error: new Error('Failed to fetch') }
        : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('B999'));

    expect(screen.getByTestId('terminal-error')).toBeInTheDocument();
    expect(
      screen.getByText('Nepodařilo se spojit se serverem. Zkuste to znovu.'),
    ).toBeInTheDocument();
  });

  it('clears the error and shows box detail after a successful next scan', () => {
    mockHook.mockImplementation((code: string | null) => {
      if (code === 'BAD') {
        return { data: null, isFetching: false, isError: true, error: new Error('Not found') };
      }
      if (code === 'GOOD') {
        return { data: fakeBox, isFetching: false, isError: false };
      }
      return { data: undefined, isFetching: false, isError: false };
    });
    render(<TransportBoxCheck />);

    act(() => scan('BAD'));
    expect(screen.getByTestId('terminal-error')).toBeInTheDocument();

    act(() => scan('GOOD'));
    expect(screen.queryByTestId('terminal-error')).not.toBeInTheDocument();
    expect(screen.getByText('B001')).toBeInTheDocument();
  });

  it('does not render a toast when the hook returns an error', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: true, error: new Error('Box nenalezen') }
        : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('B999'));

    // No element with role="alert" (toast pattern) should be present
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a load-error message when the hook reports an error', () => {
    mockHook.mockImplementation((code: string | null) =>
      code ? { data: undefined, isFetching: false, isError: true, refetch: jest.fn() } : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('B986'));

    expect(screen.getByTestId('box-load-error')).toBeInTheDocument();
    expect(screen.getByText('Chyba při načítání boxu B986')).toBeInTheDocument();
    expect(screen.queryByTestId('box-not-found')).not.toBeInTheDocument();
  });

  it('re-scanning the same error code calls refetch instead of deduplicating', () => {
    const refetch = jest.fn();
    mockHook.mockImplementation((code: string | null) =>
      code ? { data: undefined, isFetching: false, isError: true, refetch } : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('B986'));
    act(() => scan('B986'));

    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('keyboard toggle flips the input mode between none and text', () => {
    mockHook.mockReturnValue({ data: undefined, isFetching: false, isError: false });
    render(<TransportBoxCheck />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveAttribute('inputmode', 'none');

    fireEvent.click(screen.getByRole('button', { name: /zobrazit klávesnici/i }));
    expect(input).toHaveAttribute('inputmode', 'text');

    fireEvent.click(screen.getByRole('button', { name: /skrýt klávesnici/i }));
    expect(input).toHaveAttribute('inputmode', 'none');
  });
});
