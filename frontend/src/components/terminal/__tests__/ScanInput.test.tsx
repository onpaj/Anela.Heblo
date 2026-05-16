import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import ScanInput from '../ScanInput';

beforeEach(() => {
  jest.useFakeTimers();
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

describe('ScanInput', () => {
  const onScan = jest.fn();

  beforeEach(() => {
    onScan.mockClear();
  });

  it('auto-focuses the input on mount', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveFocus();
  });

  it('uppercases input value by default', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'abc123' } });
    expect(input).toHaveValue('ABC123');
  });

  it('does not uppercase when uppercase=false', () => {
    render(<ScanInput label="Kód" onScan={onScan} uppercase={false} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'abc' } });
    expect(input).toHaveValue('abc');
  });

  it('calls onScan and clears input on Enter', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'B001' } });
    fireEvent.submit(input.form!);
    expect(onScan).toHaveBeenCalledWith('B001');
    expect(input).toHaveValue('');
  });

  it('does not call onScan when input is empty', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.submit(input.form!);
    expect(onScan).not.toHaveBeenCalled();
  });

  it('disables input when loading=true', () => {
    render(<ScanInput label="Kód" onScan={onScan} loading={true} />);
    expect(screen.getByRole('textbox')).toBeDisabled();
  });

  it('re-focuses input 100ms after blur', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    fireEvent.blur(input);
    act(() => { jest.advanceTimersByTime(100); });
    expect(input).toHaveFocus();
  });

  it('does not re-focus on blur when refocusOnBlur=false', () => {
    // autoFocusOnMount=false prevents the mount effect from being deferred into
    // the act() call, which would re-focus an enabled input regardless of refocusOnBlur.
    render(<ScanInput label="Kód" onScan={onScan} refocusOnBlur={false} autoFocusOnMount={false} />);
    const input = screen.getByRole('textbox');
    fireEvent.blur(input);
    act(() => { jest.advanceTimersByTime(100); });
    expect(document.activeElement).not.toBe(input);
  });

  it('does not re-focus on blur when loading=true', () => {
    render(<ScanInput label="Kód" onScan={onScan} loading={true} />);
    const input = screen.getByRole('textbox');
    input.focus();
    fireEvent.blur(input);
    act(() => { jest.advanceTimersByTime(100); });
    expect(input).not.toHaveFocus();
  });

  it('re-focuses the input when loading transitions from true to false', () => {
    const { rerender } = render(<ScanInput label="Kód" onScan={onScan} loading={true} />);
    const input = screen.getByRole('textbox');
    rerender(<ScanInput label="Kód" onScan={onScan} loading={false} />);
    act(() => { jest.advanceTimersByTime(100); });
    expect(input).toHaveFocus();
  });
});
