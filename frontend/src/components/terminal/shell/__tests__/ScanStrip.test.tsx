// shell/__tests__/ScanStrip.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { ScanEchoContext } from '../ScanProvider';
import { ScanStrip } from '../ScanStrip';

it('shows the ready state when buffer + lastCode are empty', () => {
  render(
    <ScanEchoContext.Provider value={{ buffer: '', lastCode: null, lastTone: null }}>
      <ScanStrip />
    </ScanEchoContext.Provider>,
  );
  expect(screen.getByTestId('scan-strip')).toHaveTextContent(/Připraveno ke skenování/i);
});

it('echoes the last code with its tone', () => {
  render(
    <ScanEchoContext.Provider value={{ buffer: '', lastCode: 'B001', lastTone: 'ok' }}>
      <ScanStrip />
    </ScanEchoContext.Provider>,
  );
  expect(screen.getByText('B001')).toBeInTheDocument();
});
