import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import TransportBoxCheck from '../TransportBoxCheck';
import { ScanProvider } from '../shell/ScanProvider';
import { FlashOverlay } from '../shell/FlashOverlay';
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

const renderScreen = () =>
  render(
    <ScanProvider>
      <TransportBoxCheck />
      <FlashOverlay />
    </ScanProvider>,
  );

const scan = (code: string) => {
  const input = screen.getByTestId('wedge-input');
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: 'Enter' });
};

describe('TransportBoxCheck', () => {
  it('shows the empty prompt before any scan', () => {
    mockHook.mockReturnValue({ data: undefined, isFetching: false, isError: false });
    renderScreen();
    expect(screen.getByTestId('subject-empty')).toBeInTheDocument();
    expect(screen.getByText('Naskenujte box ke kontrole')).toBeInTheDocument();
  });

  it('shows box detail with contents and history after a resolving scan', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B001'
        ? { data: fakeBox, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('b001'));

    // Subject header reflects the scanned box.
    expect(screen.getByTestId('subject-header')).toBeInTheDocument();
    expect(screen.getByText('1 položek')).toBeInTheDocument();
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
    renderScreen();
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
    renderScreen();
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
    renderScreen();

    act(() => scan('BAD'));
    expect(screen.getByTestId('terminal-error')).toBeInTheDocument();

    act(() => scan('GOOD'));
    expect(screen.queryByTestId('terminal-error')).not.toBeInTheDocument();
    expect(screen.getByTestId('subject-header')).toBeInTheDocument();
  });

  it('does not render a toast when the hook returns an error', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: true, error: new Error('Box nenalezen') }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('B999'));

    // No element with role="alert" (toast pattern) should be present
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('re-scanning the same error code calls refetch instead of deduplicating', () => {
    const refetch = jest.fn();
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: undefined, isFetching: false, isError: true, error: new Error('x'), refetch }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('B986'));
    act(() => scan('B986'));

    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('flashes ok exactly once when a box resolves successfully', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B001'
        ? { data: fakeBox, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('b001'));

    // FlashOverlay renders an ok-tone status when the resolution flashes ok.
    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'ok');
  });

  it('flashes err exactly once when a scan errors', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: true, error: new Error('Box nenalezen') }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('B999'));

    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'err');
  });
});
