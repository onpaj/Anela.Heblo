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
  fireEvent.change(screen.getByRole('textbox'), { target: { value: code } });
  fireEvent.submit(screen.getByRole('form'));
};

describe('TransportBoxCheck', () => {
  it('renders a focused scan input on mount', () => {
    mockHook.mockReturnValue({ data: undefined, isFetching: false });
    render(<TransportBoxCheck />);
    expect(screen.getByRole('textbox')).toHaveFocus();
  });

  it('shows box detail with contents and history after a resolving scan', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B001'
        ? { data: fakeBox, isFetching: false }
        : { data: undefined, isFetching: false },
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

  it('shows a not-found message for an unknown code', () => {
    mockHook.mockImplementation((code: string | null) =>
      code ? { data: null, isFetching: false } : { data: undefined, isFetching: false },
    );
    render(<TransportBoxCheck />);
    act(() => scan('B999'));

    expect(screen.getByTestId('box-not-found')).toBeInTheDocument();
    expect(screen.getByText('Box B999 nenalezen')).toBeInTheDocument();
  });

  it('keyboard toggle flips the input mode between none and text', () => {
    mockHook.mockReturnValue({ data: undefined, isFetching: false });
    render(<TransportBoxCheck />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveAttribute('inputmode', 'none');

    fireEvent.click(screen.getByRole('button', { name: /zobrazit klávesnici/i }));
    expect(input).toHaveAttribute('inputmode', 'text');

    fireEvent.click(screen.getByRole('button', { name: /skrýt klávesnici/i }));
    expect(input).toHaveAttribute('inputmode', 'none');
  });
});
