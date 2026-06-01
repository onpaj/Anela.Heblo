import React from 'react';
import { render, screen, fireEvent, act, waitFor } from '@testing-library/react';
import TransportBoxReceive from '../TransportBoxReceive';
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

const scan = (code: string) => {
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: code } });
  fireEvent.submit(input.form!);
};

const byCodeFor = (target: string, box: Record<string, unknown> | null) => (code: string | null) =>
  code === target
    ? { data: box, isFetching: false }
    : { data: undefined, isFetching: false };

describe('TransportBoxReceive', () => {
  it('renders a focused scan input on mount', () => {
    mockByCode.mockReturnValue({ data: undefined, isFetching: false });
    render(<TransportBoxReceive />);
    expect(screen.getByRole('textbox')).toHaveFocus();
  });

  it('shows box detail and an enabled Accept button after a receivable scan', () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('b001'));

    expect(screen.getByText('B001')).toBeInTheDocument();
    expect(screen.getByText('Obvazy')).toBeInTheDocument();
    expect(screen.getByTestId('accept-box')).toBeEnabled();
    expect(screen.getByTestId('reject-box')).toBeEnabled();
  });

  it('accepts a box: calls the state-change mutation and shows the success banner', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    fireEvent.click(screen.getByTestId('accept-box'));

    expect(mutateAsync).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' });
    await waitFor(() => {
      expect(screen.getByTestId('receive-success')).toBeInTheDocument();
    });
    expect(screen.getByText('Box B001 přijat')).toBeInTheDocument();
  });

  it('re-scanning the loaded box code triggers Accept without a tap', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    await act(async () => {
      scan('B001');
    });

    expect(mutateAsync).toHaveBeenCalledWith({ boxId: 1, newState: 'Received' });
  });

  it('Reject clears the box without calling the backend', () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    fireEvent.click(screen.getByTestId('reject-box'));

    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.queryByText('B001')).not.toBeInTheDocument();
  });

  it('disables Accept and shows a warning for a non-receivable box', () => {
    mockByCode.mockImplementation(byCodeFor('B002', nonReceivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B002'));

    expect(screen.getByTestId('accept-box')).toBeDisabled();
    expect(screen.getByTestId('not-receivable')).toBeInTheDocument();

    // re-scanning a non-receivable box must NOT trigger Accept
    act(() => scan('B002'));
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('shows a not-found message for an unknown code', () => {
    mockByCode.mockImplementation((code: string | null) =>
      code ? { data: null, isFetching: false } : { data: undefined, isFetching: false },
    );
    render(<TransportBoxReceive />);
    act(() => scan('B999'));

    expect(screen.getByTestId('box-not-found')).toBeInTheDocument();
    expect(screen.getByText('Box B999 nenalezen')).toBeInTheDocument();
  });

  it('shows a load-error message when the hook reports an error', () => {
    mockByCode.mockImplementation((code: string | null) =>
      code ? { data: undefined, isFetching: false, isError: true, refetch: jest.fn() } : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxReceive />);
    act(() => scan('B986'));

    expect(screen.getByTestId('box-load-error')).toBeInTheDocument();
    expect(screen.getByText('Chyba při načítání boxu B986')).toBeInTheDocument();
    expect(screen.queryByTestId('box-not-found')).not.toBeInTheDocument();
  });

  it('re-scanning the same error code calls refetch instead of deduplicating', () => {
    const refetch = jest.fn();
    mockByCode.mockImplementation((code: string | null) =>
      code ? { data: undefined, isFetching: false, isError: true, refetch } : { data: undefined, isFetching: false, isError: false },
    );
    render(<TransportBoxReceive />);
    act(() => scan('B986'));
    act(() => scan('B986'));

    expect(refetch).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('box-load-error')).toBeInTheDocument();
  });

  it('hides the success banner after SUCCESS_DISPLAY_MS elapses', async () => {
    mockByCode.mockImplementation(byCodeFor('B001', receivableBox));
    render(<TransportBoxReceive />);
    act(() => scan('B001'));

    fireEvent.click(screen.getByTestId('accept-box'));

    await waitFor(() => {
      expect(screen.getByTestId('receive-success')).toBeInTheDocument();
    });

    act(() => { jest.advanceTimersByTime(2500); });
    expect(screen.queryByTestId('receive-success')).not.toBeInTheDocument();
  });
});
