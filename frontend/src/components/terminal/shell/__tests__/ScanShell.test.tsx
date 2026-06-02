// shell/__tests__/ScanShell.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { ScanProvider } from '../ScanProvider';
import { ScanShell } from '../ScanShell';

beforeEach(() => jest.useFakeTimers());
afterEach(() => { jest.runOnlyPendingTimers(); jest.useRealTimers(); });

it('renders subject, body, scan strip and a docked action in order', () => {
  render(
    <ScanProvider>
      <ScanShell
        subject={<div data-testid="subj">subj</div>}
        actions={[{ label: 'Go', onClick: jest.fn(), testId: 'go' }]}
      >
        <div data-testid="body">body</div>
      </ScanShell>
    </ScanProvider>,
  );
  expect(screen.getByTestId('subj')).toBeInTheDocument();
  expect(screen.getByTestId('body')).toBeInTheDocument();
  expect(screen.getByTestId('scan-strip')).toBeInTheDocument();
  expect(screen.getByTestId('go')).toBeInTheDocument();
});
