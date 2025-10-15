import React from 'react';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { ManualActionRequiredTile } from '../ManualActionRequiredTile';

// Helper to render with Router context
const renderWithRouter = (component: React.ReactElement) => {
  return render(<BrowserRouter>{component}</BrowserRouter>);
};

describe('ManualActionRequiredTile', () => {
  it('renders zero count when no manual actions required', () => {
    const data = {
      status: 'success',
      data: {
        count: 0,
        date: '2023-10-15T14:30:00Z'
      }
    };

    renderWithRouter(<ManualActionRequiredTile data={data} />);

    // Check if count is displayed
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('renders positive count when manual actions required', () => {
    const data = {
      status: 'success',
      data: {
        count: 3,
        date: '2023-10-15T14:30:00Z'
      }
    };

    renderWithRouter(<ManualActionRequiredTile data={data} />);

    // Check if count is displayed
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('renders count of 1', () => {
    const data = {
      status: 'success',
      data: {
        count: 1,
        date: '2023-10-15T14:30:00Z'
      }
    };

    renderWithRouter(<ManualActionRequiredTile data={data} />);

    // Check if count is displayed
    expect(screen.getByText('1')).toBeInTheDocument();
  });

  it('renders error state when data has error', () => {
    const data = {
      status: 'error',
      error: 'Failed to load data'
    };

    renderWithRouter(<ManualActionRequiredTile data={data} />);

    // Check if error message is displayed
    expect(screen.getByText('Failed to load data')).toBeInTheDocument();
    
    // Check if error icon is displayed
    expect(screen.getByText('⚠️')).toBeInTheDocument();
  });

  it('renders zero count when count is undefined', () => {
    const data = {
      status: 'success',
      data: {
        date: '2023-10-15T14:30:00Z'
        // count is undefined
      }
    };

    renderWithRouter(<ManualActionRequiredTile data={data} />);

    // Check if zero count is displayed (fallback)
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('renders zero count when data is undefined', () => {
    const data = {
      status: 'success'
      // data is undefined
    };

    renderWithRouter(<ManualActionRequiredTile data={data} />);

    // Check if zero count is displayed (fallback)
    expect(screen.getByText('0')).toBeInTheDocument();
  });
});