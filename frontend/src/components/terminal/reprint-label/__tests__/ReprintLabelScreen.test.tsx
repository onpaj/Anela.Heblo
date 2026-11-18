import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import ReprintLabelScreen from '../ReprintLabelScreen';
import { ScanProvider, ScanEchoContext } from '../../shell/ScanProvider';
import { useReprintMaterialContainerLabel } from '../../../../api/hooks/useMaterialContainers';
import { SwaggerException } from '../../../../api/generated/api-client';

jest.mock('../../../../api/hooks/useMaterialContainers', () => ({
  useReprintMaterialContainerLabel: jest.fn(),
}));

const mockHook = useReprintMaterialContainerLabel as jest.Mock;

type Callbacks = { onSuccess?: (r: unknown) => void; onError?: (e: unknown) => void };
let mutate: jest.Mock;

beforeEach(() => {
  jest.useFakeTimers();
  mutate = jest.fn();
  mockHook.mockReturnValue({ mutate, isPending: false });
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

const FlashProbe: React.FC = () => {
  const { lastTone, lastCode } = React.useContext(ScanEchoContext);
  return <div data-testid="flash-probe">{`${lastTone ?? ''}:${lastCode ?? ''}`}</div>;
};

const flashState = () => screen.getByTestId('flash-probe').textContent;

const renderScreen = () =>
  render(
    <ScanProvider>
      <FlashProbe />
      <ReprintLabelScreen />
    </ScanProvider>,
  );

const scan = (code: string) => {
  const input = screen.getByTestId('wedge-input');
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: 'Enter' });
};

const lastCallbacks = (): Callbacks => mutate.mock.calls[mutate.mock.calls.length - 1][1];

describe('ReprintLabelScreen', () => {
  it('scan reprints the scanned code', () => {
    renderScreen();
    scan(' m00000010 ');
    expect(mutate).toHaveBeenCalledTimes(1);
    expect(mutate.mock.calls[0][0]).toEqual({ code: 'M00000010', mediaChangeConfirmed: false });
  });

  it('success adds code to history', () => {
    renderScreen();
    scan('M00000010');
    act(() => lastCallbacks().onSuccess?.({ requiresMediaChangeConfirmation: false }));
    expect(screen.getByTestId('reprint-history')).toHaveTextContent('M00000010');
    expect(screen.getByTestId('reprint-history')).toHaveTextContent('✓');
    expect(flashState()).toBe('ok:M00000010');
  });

  it('media change response opens sheet; confirm resends with mediaChangeConfirmed true; next scan also sends true', () => {
    renderScreen();
    scan('M00000010');
    act(() => lastCallbacks().onSuccess?.({ requiresMediaChangeConfirmation: true }));
    expect(screen.getByText('Vyměňte roli za štítky kontejnerů')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Role vyměněna, tisknout'));
    expect(mutate).toHaveBeenCalledTimes(2);
    expect(mutate.mock.calls[1][0]).toEqual({ code: 'M00000010', mediaChangeConfirmed: true });
    expect(screen.queryByText('Vyměňte roli za štítky kontejnerů')).not.toBeInTheDocument();

    act(() => lastCallbacks().onSuccess?.({ requiresMediaChangeConfirmation: false }));
    scan('M00000011');
    expect(mutate.mock.calls[2][0]).toEqual({ code: 'M00000011', mediaChangeConfirmed: true });
  });

  it('scan while pending is ignored', () => {
    renderScreen();
    scan('M00000010');
    scan('M00000011');
    expect(mutate).toHaveBeenCalledTimes(1);
    expect(flashState()).toBe('warn:M00000011');
  });

  it('scan while sheet open is ignored and pending code unchanged', () => {
    renderScreen();
    scan('M00000010');
    act(() => lastCallbacks().onSuccess?.({ requiresMediaChangeConfirmation: true }));
    scan('M00000011');
    expect(mutate).toHaveBeenCalledTimes(1);
    expect(flashState()).toBe('warn:M00000011');
    fireEvent.click(screen.getByText('Role vyměněna, tisknout'));
    expect(mutate.mock.calls[1][0]).toEqual({ code: 'M00000010', mediaChangeConfirmed: true });
  });

  it('cancel closes the sheet and allows a new scan', () => {
    renderScreen();
    scan('M00000010');
    act(() => lastCallbacks().onSuccess?.({ requiresMediaChangeConfirmation: true }));
    fireEvent.click(screen.getByText('Zrušit'));
    scan('M00000011');
    expect(mutate.mock.calls[1][0]).toEqual({ code: 'M00000011', mediaChangeConfirmed: false });
  });

  it('error shows resolved message', () => {
    renderScreen();
    scan('M00000010');
    const error = new SwaggerException('err', 409, JSON.stringify({ errorCode: 'MaterialContainerDiscarded' }), {}, null);
    act(() => lastCallbacks().onError?.(error));
    expect(screen.getByText('Kontejner je vyřazený – štítek nelze dotisknout.')).toBeInTheDocument();
    expect(screen.getByTestId('reprint-history')).toHaveTextContent('✗');
    expect(flashState()).toBe('err:M00000010');
  });
});
