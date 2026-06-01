// shell/__tests__/BottomSheet.test.tsx
import React, { useContext } from 'react';
import { render } from '@testing-library/react';
import { ScanProvider, ScanActionsContext } from '../ScanProvider';
import { BottomSheet } from '../BottomSheet';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

it('sets yieldFocus while open and releases on unmount', () => {
  const spy = jest.fn();
  function Capture() {
    const a = useContext(ScanActionsContext)!;
    // wrap setYieldFocus to observe calls
    const orig = a.setYieldFocus;
    a.setYieldFocus = (v: boolean) => { spy(v); orig(v); };
    return null;
  }
  const { rerender } = render(
    <ScanProvider>
      <Capture />
      <BottomSheet open onClose={jest.fn()} hasInput><input /></BottomSheet>
    </ScanProvider>,
  );
  expect(spy).toHaveBeenCalledWith(true);
  rerender(
    <ScanProvider>
      <Capture />
      <BottomSheet open={false} onClose={jest.fn()} hasInput><input /></BottomSheet>
    </ScanProvider>,
  );
  expect(spy).toHaveBeenCalledWith(false);
});
