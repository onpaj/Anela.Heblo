import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import RunningJobIndicator from '../RunningJobIndicator';

// Mock the hook
const mockUseRunningJobsForGiftPackage = jest.fn();

jest.mock('../../../../api/hooks/useGiftPackageManufacturing', () => ({
  useRunningJobsForGiftPackage: () => mockUseRunningJobsForGiftPackage()
}));

const createTestQueryClient = () => new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
    },
  },
});

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = createTestQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      {component}
    </QueryClientProvider>
  );
};

describe('RunningJobIndicator', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders nothing when no running jobs', () => {
    mockUseRunningJobsForGiftPackage.mockReturnValue({
      data: { hasRunningJobs: false, runningJobs: [] }
    });

    const { container } = renderWithQueryClient(
      <RunningJobIndicator giftPackageCode="TEST001" />
    );

    expect(container.firstChild).toBeNull();
  });

  it('renders red dot when there are running jobs', () => {
    mockUseRunningJobsForGiftPackage.mockReturnValue({
      data: { 
        hasRunningJobs: true, 
        runningJobs: [
          { jobId: 'job-1', status: 'Processing', isRunning: true }
        ] 
      }
    });

    renderWithQueryClient(
      <RunningJobIndicator giftPackageCode="TEST001" />
    );

    // Check if the red dot is rendered
    const indicator = document.querySelector('.bg-red-500');
    expect(indicator).toBeInTheDocument();
    expect(indicator).toHaveClass('animate-pulse');
  });

  it('renders counter when multiple jobs are running', () => {
    mockUseRunningJobsForGiftPackage.mockReturnValue({
      data: { 
        hasRunningJobs: true, 
        runningJobs: [
          { jobId: 'job-1', status: 'Processing', isRunning: true },
          { jobId: 'job-2', status: 'Enqueued', isRunning: true },
          { jobId: 'job-3', status: 'Processing', isRunning: true }
        ] 
      }
    });

    renderWithQueryClient(
      <RunningJobIndicator giftPackageCode="TEST001" />
    );

    // Check if the counter is displayed
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('shows correct tooltip for single job', () => {
    mockUseRunningJobsForGiftPackage.mockReturnValue({
      data: { 
        hasRunningJobs: true, 
        runningJobs: [
          { jobId: 'job-1', status: 'Processing', isRunning: true }
        ] 
      }
    });

    renderWithQueryClient(
      <RunningJobIndicator giftPackageCode="TEST001" />
    );

    const indicator = document.querySelector('.bg-red-500');
    expect(indicator).toHaveAttribute('title', '1 běžící výroba');
  });

  it('shows correct tooltip for multiple jobs', () => {
    mockUseRunningJobsForGiftPackage.mockReturnValue({
      data: { 
        hasRunningJobs: true, 
        runningJobs: [
          { jobId: 'job-1', status: 'Processing', isRunning: true },
          { jobId: 'job-2', status: 'Enqueued', isRunning: true }
        ] 
      }
    });

    renderWithQueryClient(
      <RunningJobIndicator giftPackageCode="TEST001" />
    );

    const indicator = document.querySelector('.bg-red-500');
    expect(indicator).toHaveAttribute('title', '2 běžící výroby');
  });
});