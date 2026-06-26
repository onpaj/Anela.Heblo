import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import TerminalHome from '../TerminalHome';
import { ScanProvider } from '../shell/ScanProvider';
import { FlashOverlay } from '../shell/FlashOverlay';
import { useTransportBoxByCodeQuery } from '../../../api/hooks/useTransportBoxes';

jest.mock('../../../api/hooks/useTransportBoxes', () => ({
  useTransportBoxByCodeQuery: jest.fn(),
}));

const mockHook = useTransportBoxByCodeQuery as jest.Mock;

// Renders the current path so navigation assertions can read the destination.
const LocationProbe: React.FC = () => {
  const location = useLocation();
  return <div data-testid="location-display">{location.pathname}</div>;
};

beforeEach(() => {
  jest.useFakeTimers();
  mockHook.mockReset();
  mockHook.mockReturnValue({ data: undefined, isFetching: false, isError: false });
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

const renderHome = () =>
  render(
    <MemoryRouter initialEntries={['/terminal']}>
      <ScanProvider>
        <Routes>
          <Route path="/terminal" element={<TerminalHome />} />
          <Route path="/terminal/box-check" element={<LocationProbe />} />
          <Route path="/terminal/box-fill" element={<LocationProbe />} />
          <Route path="/terminal/receive" element={<LocationProbe />} />
        </Routes>
        <FlashOverlay />
      </ScanProvider>
    </MemoryRouter>,
  );

const scan = (code: string) => {
  const input = screen.getByTestId('wedge-input');
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: 'Enter' });
};

describe('TerminalHome', () => {
  it('renders heading', () => {
    renderHome();
    expect(screen.getByText('Vyberte operaci')).toBeInTheDocument();
  });

  it('renders all five workflow tiles', () => {
    renderHome();
    expect(screen.getByTestId('workflow-tile-box-check')).toHaveAttribute('href', '/terminal/box-check');
    expect(screen.getByTestId('workflow-tile-box-fill')).toHaveAttribute('href', '/terminal/box-fill');
    expect(screen.getByTestId('workflow-tile-receive')).toHaveAttribute('href', '/terminal/receive');
    expect(screen.getByTestId('workflow-tile-stocktake')).toHaveAttribute('href', '/terminal/stocktake');
    expect(screen.getByTestId('workflow-tile-lot-identification')).toHaveAttribute(
      'href',
      '/terminal/lot-identification',
    );
  });

  it('shows coming-soon label only on the stub tiles', () => {
    renderHome();
    const labels = screen.getAllByText('Brzy k dispozici');
    expect(labels).toHaveLength(1);
  });

  it('navigates to receive when a scanned box is receivable', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B001'
        ? { data: { code: 'B001', state: 'Received', isReceivable: true }, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderHome();
    act(() => scan('b001'));

    expect(screen.getByTestId('location-display')).toHaveTextContent('/terminal/receive');
  });

  it('navigates to box-fill when a scanned box is open and not receivable', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B002'
        ? { data: { code: 'B002', state: 'Opened', isReceivable: false }, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderHome();
    act(() => scan('b002'));

    expect(screen.getByTestId('location-display')).toHaveTextContent('/terminal/box-fill');
  });

  it('navigates to box-check for any other box state', () => {
    mockHook.mockImplementation((code: string | null) =>
      code === 'B003'
        ? { data: { code: 'B003', state: 'Stocked', isReceivable: false }, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderHome();
    act(() => scan('b003'));

    expect(screen.getByTestId('location-display')).toHaveTextContent('/terminal/box-check');
  });

  it('stays on Home and flashes err when the code is not found', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: false }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderHome();
    act(() => scan('B999'));

    expect(screen.queryByTestId('location-display')).not.toBeInTheDocument();
    expect(screen.getByText('Vyberte operaci')).toBeInTheDocument();
    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'err');
  });

  it('stays on Home and flashes err when the lookup errors', () => {
    mockHook.mockImplementation((code: string | null) =>
      code
        ? { data: null, isFetching: false, isError: true, error: new Error('x') }
        : { data: undefined, isFetching: false, isError: false },
    );
    renderHome();
    act(() => scan('B998'));

    expect(screen.queryByTestId('location-display')).not.toBeInTheDocument();
    expect(screen.getByTestId('flash-overlay')).toHaveAttribute('data-tone', 'err');
  });
});
