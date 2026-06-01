import React, { useContext } from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { ScanProvider, ScanActionsContext } from '../ScanProvider';
import { FlashOverlay } from '../FlashOverlay';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

function FlashButton() {
  const actions = useContext(ScanActionsContext)!;
  return <button onClick={() => actions.flash('err', 'B999')}>flash</button>;
}

it('renders a non-blocking, aria-live overlay with the err tone after flash()', () => {
  render(<ScanProvider><FlashOverlay /><FlashButton /></ScanProvider>);
  act(() => { fireEvent.click(screen.getByText('flash')); });
  const overlay = screen.getByTestId('flash-overlay');
  expect(overlay).toHaveAttribute('aria-live');
  expect(overlay.className).toContain('pointer-events-none');
  expect(overlay.dataset.tone).toBe('err');
});
