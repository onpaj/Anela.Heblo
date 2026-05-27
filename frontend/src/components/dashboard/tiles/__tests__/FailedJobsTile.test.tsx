import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FailedJobsTile } from '../FailedJobsTile';

// window.location.href is read-only in jsdom; replace with a writable stub
const originalLocation = window.location;
beforeAll(() => {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { href: '' },
  });
});
afterAll(() => {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: originalLocation,
  });
});
beforeEach(() => {
  (window.location as { href: string }).href = '';
});

describe('FailedJobsTile', () => {
  it('renders error state without a clickable wrapper', () => {
    render(<FailedJobsTile data={{ status: 'error', error: 'storage unavailable' }} />);

    expect(screen.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.queryByTestId('failed-jobs-tile')).toBeNull();
  });

  it('renders count 0 with red styling', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 0 } }} />);

    expect(screen.getByTestId('failed-jobs-tile')).toBeInTheDocument();
    expect(screen.getByText('0')).toBeInTheDocument();
    expect(screen.getByText('failed jobs')).toBeInTheDocument();
  });

  it('renders non-zero count', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 12 } }} />);

    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('failed jobs')).toBeInTheDocument();
  });

  it('navigates to /hangfire/jobs/failed on click', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 3 } }} />);

    fireEvent.click(screen.getByTestId('failed-jobs-tile'));

    expect(window.location.href).toBe('/hangfire/jobs/failed');
  });
});
