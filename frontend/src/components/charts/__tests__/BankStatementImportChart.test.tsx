import React from 'react';
import { render } from '@testing-library/react';
import { BankStatementImportChart } from '../BankStatementImportChart';
import { GRAPHITE } from '../../common/reactSelectDarkStyles';
import type { DailyBankStatementStatistics } from '../../../api/hooks/useBankStatements';

// jsdom reports 0 width/height for the container, so recharts' ResponsiveContainer
// never renders its children. Bypass it with a fixed-size wrapper so the SVG
// primitives under test actually mount.
jest.mock('recharts', () => {
  const actual = jest.requireActual('recharts');
  const ReactLib = require('react');
  return {
    ...actual,
    ResponsiveContainer: ({ children }: any) =>
      ReactLib.createElement(
        'div',
        { style: { width: 800, height: 400 } },
        ReactLib.cloneElement(children, { width: 800, height: 400 }),
      ),
  };
});

jest.mock('../../../contexts/ThemeContext', () => ({
  useTheme: jest.fn(),
}));

const { useTheme } = jest.requireMock('../../../contexts/ThemeContext') as {
  useTheme: jest.Mock;
};

const buildData = (): DailyBankStatementStatistics[] =>
  [
    { date: new Date('2026-07-17'), importCount: 6, totalItemCount: 12 }, // Friday
    { date: new Date('2026-07-18'), importCount: 1, totalItemCount: 2 }, // Saturday, below threshold
    { date: new Date('2026-07-19'), importCount: 1, totalItemCount: 2 }, // Sunday, below threshold
    { date: new Date('2026-07-20'), importCount: 6, totalItemCount: 12 }, // Monday
  ] as any;

describe('BankStatementImportChart theme-aware colors', () => {
  it('renders light-mode colors unchanged when theme is light', () => {
    useTheme.mockReturnValue({ theme: 'light', toggle: jest.fn() });
    const { container } = render(
      <BankStatementImportChart data={buildData()} minimumThreshold={5} />,
    );

    expect(
      container.querySelector('.recharts-cartesian-grid line')?.getAttribute('stroke'),
    ).toBe('#f0f0f0');
    expect(
      container.querySelector('.recharts-cartesian-axis-line')?.getAttribute('stroke'),
    ).toBe('#6b7280');
    expect(
      container.querySelector('.recharts-reference-area-rect')?.getAttribute('fill'),
    ).toBe('#e0f2fe');
    expect(
      container.querySelector('.recharts-reference-line-line')?.getAttribute('stroke'),
    ).toBe('#dc2626');
    expect(
      container.querySelector('.recharts-line-curve')?.getAttribute('stroke'),
    ).toBe('#3b82f6');
    expect(container.querySelector('circle[r="4"]')?.getAttribute('fill')).toBe('#dc2626');
    expect(container.querySelector('circle[r="4"]')?.getAttribute('stroke')).toBe('#fff');
  });

  it('switches to dark-mode colors when theme is dark', () => {
    useTheme.mockReturnValue({ theme: 'dark', toggle: jest.fn() });
    const { container } = render(
      <BankStatementImportChart data={buildData()} minimumThreshold={5} />,
    );

    expect(
      container.querySelector('.recharts-cartesian-grid line')?.getAttribute('stroke'),
    ).toBe(GRAPHITE.border);
    expect(
      container.querySelector('.recharts-cartesian-axis-line')?.getAttribute('stroke'),
    ).toBe(GRAPHITE.muted);
    expect(
      container.querySelector('.recharts-reference-area-rect')?.getAttribute('fill'),
    ).toBe('#0ea5e9');
    expect(
      container.querySelector('.recharts-reference-line-line')?.getAttribute('stroke'),
    ).toBe('#f87171');
    // Line stroke is intentionally the same readable blue in both themes.
    expect(
      container.querySelector('.recharts-line-curve')?.getAttribute('stroke'),
    ).toBe('#3b82f6');
    expect(container.querySelector('circle[r="4"]')?.getAttribute('fill')).toBe('#f87171');
    expect(container.querySelector('circle[r="4"]')?.getAttribute('stroke')).toBe(
      GRAPHITE.surface,
    );
  });
});
