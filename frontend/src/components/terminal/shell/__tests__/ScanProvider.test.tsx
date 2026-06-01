// shell/__tests__/ScanProvider.test.tsx
import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { ScanProvider } from '../ScanProvider';
import { ScanStrip } from '../ScanStrip';
import { useScanScreen } from '../useScanScreen';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

function Probe({ onScan }: { onScan: (c: string) => void }) {
  useScanScreen({ onScan });
  return <div>probe</div>;
}

function OtherProbe() {
  useScanScreen({ onScan: jest.fn() });
  return <div>other</div>;
}

function typeCode(code: string) {
  const input = screen.getByTestId('wedge-input') as HTMLInputElement;
  fireEvent.change(input, { target: { value: code } });
  fireEvent.keyDown(input, { key: 'Enter' });
}

describe('ScanProvider wedge', () => {
  it('dispatches trimmed, uppercased buffer to the active screen on Enter', () => {
    const onScan = jest.fn();
    render(<ScanProvider><Probe onScan={onScan} /></ScanProvider>);
    act(() => typeCode('  b001  '));
    expect(onScan).toHaveBeenCalledWith('B001');
  });

  it('clears the buffer after dispatch', () => {
    const onScan = jest.fn();
    render(<ScanProvider><Probe onScan={onScan} /></ScanProvider>);
    act(() => typeCode('b001'));
    const input = screen.getByTestId('wedge-input') as HTMLInputElement;
    expect(input.value).toBe('');
  });

  it('keeps a focused capture field by default', () => {
    render(<ScanProvider><Probe onScan={jest.fn()} /></ScanProvider>);
    const input = screen.getByTestId('wedge-input');
    expect(input).toHaveFocus();
  });

  it('clears the scan echo when a new screen registers (screen entry)', () => {
    const { rerender } = render(
      <ScanProvider><Probe onScan={jest.fn()} /><ScanStrip /></ScanProvider>,
    );
    act(() => typeCode('B001'));
    expect(screen.getByTestId('scan-strip')).toHaveTextContent('B001');

    // Navigating to another screen swaps the active scan-handler component,
    // which re-registers and must reset the strip so it no longer shows B001.
    rerender(<ScanProvider><OtherProbe /><ScanStrip /></ScanProvider>);
    expect(screen.queryByText('B001')).not.toBeInTheDocument();
    expect(screen.getByTestId('scan-strip')).toHaveTextContent(/Připraveno ke skenování/i);
  });
});
