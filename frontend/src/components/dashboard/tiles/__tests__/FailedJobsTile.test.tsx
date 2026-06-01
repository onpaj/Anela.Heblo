import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FailedJobsTile } from '../FailedJobsTile';

// Mock runtimeConfig so the component gets a predictable backend URL
jest.mock('../../../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
}));

// Mock window.open
const mockWindowOpen = jest.fn();
beforeAll(() => {
  window.open = mockWindowOpen;
});
beforeEach(() => {
  mockWindowOpen.mockReset();
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

  it('opens Hangfire failed jobs page in a new tab on click', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 3 } }} />);

    fireEvent.click(screen.getByTestId('failed-jobs-tile'));

    expect(mockWindowOpen).toHaveBeenCalledWith(
      'http://localhost:5001/hangfire/jobs/failed',
      '_blank'
    );
  });
});
