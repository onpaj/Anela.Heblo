import React from 'react';
import { render, screen, fireEvent, act, waitFor } from '@testing-library/react';
import TransportBoxReceive from '../TransportBoxReceive';
import { ScanProvider } from '../shell/ScanProvider';
import { FlashOverlay } from '../shell/FlashOverlay';
import {
  useTransportBoxByCodeQuery,
  useChangeTransportBoxState,
} from '../../../api/hooks/useTransportBoxes';

jest.mock('../../../api/hooks/useTransportBoxes', () => ({
  useTransportBoxByCodeQuery: jest.fn(),
  useChangeTransportBoxState: jest.fn(),
}));

const mockByCode = useTransportBoxByCodeQuery as jest.Mock;
const mockChangeState = useChangeTransportBoxState as jest.Mock;

const receivableBox = {
  id: 1,
  code: 'B001',
  state: 'InTransit',
  isReceivable: true,
  location: 'Kumbal',
  items: [{ id: 10, productCode: 'MED001', productName: 'Obvazy', amount: 5 }],
  stateLog: [{ id: 1, state: 'InTransit', stateDate: new Date('2026-05-10'), user: 'jan' }],
};

const nonReceivableBox = { ...receivableBox, state: 'Stocked', isReceivable: false };

let mutateAsync: jest.Mock;

beforeEach(() => {
  jest.useFakeTimers();
  mockByCode.mockReset();
  mockChangeState.mockReset();
  mutateAsync = jest.fn().mockResolvedValue({});
  mockChangeState.mockReturnValue({ mutateAsync, isPending: false });
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

const renderScreen = () =>
  render(
    <ScanProvider>
      <TransportBoxReceive />
      <FlashOverlay />
    </ScanProvider>,
  );

const scan = (code: string) => {
  const input = screen.getByTestId('wedge-input');
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: 'Enter' });
};

const byCodeFor = (target: string, box: Record<string, unknown> | null) => (code: string | null) =>
  code === target
    ? { data: box, isFetching: false, isError: false }
    : { data: undefined, isFetching: false, isError: false };

describe('TransportBoxReceive', () => {
  it('shows the empty prompt before any scan', () => {
    mockByCode.mockReturnValue({ data: undefined, isFetching: false, isError: false });
    renderScreen();
    expect(screen.getByTestId('subject-empty')).toBeInTheDocument();
    expect(screen.getByText('Naskenujte box k příjmu')).toBeInTheDocument();
  });

  it('shows box detail and a split dock after a receivable scan', () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    renderScreen();
    act(() => scan('b001'));

    expect(screen.getByTestId('subject-header')).toBeInTheDocument();
    expect(screen.getByText('Připraveno k příjmu')).toBeInTheDocument();
    expect(screen.getByText('Obvazy')).toBeInTheDocument();
    expect(screen.getByTestId('reject-box')).toBeEnabled();
    expect(screen.getByTestId('accept-box')).toBeEnabled();
  });

  it('flashes ok and confirms receipt: calls the mutation and resets to empty', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    renderScreen();
    act(() => scan('B001'));

    fireEvent.click(screen.getByTestId('accept-box'));

    expect(mutateAsync).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' });

    // After the mutation resolves the in-hand box is cleared and an ok flash fires.
    await waitFor(() => {
      expect(screen.getByTestId('subject-empty')).toBeInTheDocument();
    });
    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'ok');
  });

  it('re-scanning the loaded box code triggers Accept without a tap', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    renderScreen();
    act(() => scan('B001'));

    await act(async () => {
      scan('B001');
    });

    expect(mutateAsync).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' });
  });

  it('Reject clears the box without calling the backend', () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    renderScreen();
    act(() => scan('B001'));

    fireEvent.click(screen.getByTestId('reject-box'));

    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.queryByTestId('subject-header')).not.toBeInTheDocument();
    expect(screen.getByTestId('subject-empty')).toBeInTheDocument();
  });

  it('disables Accept, shows a warning, and flashes warn for a non-receivable box', () => {
    mockByCode.mockImplementation(byCodeFor('B002', nonReceivableBox));
    renderScreen();
    act(() => scan('B002'));

    expect(screen.getByTestId('accept-box')).toBeDisabled();
    expect(screen.getByTestId('not-receivable')).toBeInTheDocument();
    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'warn');

    // re-scanning a non-receivable box must NOT trigger Accept
    act(() => scan('B002'));
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('flashes err for an unknown code', () => {
    mockByCode.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('B999'));

    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'err');
    expect(screen.getByTestId('subject-empty')).toBeInTheDocument();
  });

  it('flashes err when the hook reports an error', () => {
    mockByCode.mockImplementation((code: string | null) =>
      code
        ? { data: undefined, isFetching: false, isError: true, refetch: jest.fn() }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('B986'));

    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'err');
  });

  it('re-scanning the same error code calls refetch instead of deduplicating', () => {
    const refetch = jest.fn();
    mockByCode.mockImplementation((code: string | null) =>
      code
        ? { data: undefined, isFetching: false, isError: true, refetch }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderScreen();
    act(() => scan('B986'));
    act(() => scan('B986'));

    expect(refetch).toHaveBeenCalledTimes(1);
  });
});
