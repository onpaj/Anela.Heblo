// shell/__tests__/ScanProvider.test.tsx
import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { ScanProvider } from '../ScanProvider';
import { useScanScreen } from '../useScanScreen';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

function Probe({ onScan }: { onScan: (c: string) => void }) {
  useScanScreen({ onScan });
  return <div>probe</div>;
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
});
