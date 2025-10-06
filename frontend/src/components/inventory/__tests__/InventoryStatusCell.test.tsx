import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import InventoryStatusCell from '../InventoryStatusCell';

describe('InventoryStatusCell', () => {
  const mockOnClick = jest.fn();

  beforeEach(() => {
    mockOnClick.mockClear();
  });

  it('should render "Nikdy" for null lastStockTaking', () => {
    render(
      <InventoryStatusCell 
        lastStockTaking={null} 
        onClick={mockOnClick} 
      />
    );
    
    expect(screen.getByText('Nikdy')).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-red-500');
  });

  it('should render "Nikdy" for undefined lastStockTaking', () => {
    render(
      <InventoryStatusCell 
        lastStockTaking={undefined} 
        onClick={mockOnClick} 
      />
    );
    
    expect(screen.getByText('Nikdy')).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-red-500');
  });

  it('should render days since last stock taking with green color for recent inventory (<120 days)', () => {
    const recentDate = new Date();
    recentDate.setDate(recentDate.getDate() - 50); // 50 days ago
    
    render(
      <InventoryStatusCell 
        lastStockTaking={recentDate} 
        onClick={mockOnClick} 
      />
    );
    
    // Check for the actual text content that gets rendered (should be around 50-51 days)
    expect(screen.getByText(/5[01] d/)).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-green-500');
  });

  it('should render days since last stock taking with orange color for medium age (120-250 days)', () => {
    const mediumDate = new Date();
    mediumDate.setDate(mediumDate.getDate() - 180); // 180 days ago
    
    render(
      <InventoryStatusCell 
        lastStockTaking={mediumDate} 
        onClick={mockOnClick} 
      />
    );
    
    // Check for the actual text content that gets rendered (should be around 180-181 days)
    expect(screen.getByText(/18[01] d/)).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-orange-500');
  });

  it('should render days since last stock taking with red color for old inventory (>250 days)', () => {
    const oldDate = new Date();
    oldDate.setDate(oldDate.getDate() - 300); // 300 days ago
    
    render(
      <InventoryStatusCell 
        lastStockTaking={oldDate} 
        onClick={mockOnClick} 
      />
    );
    
    expect(screen.getByText('300 d')).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-red-500');
  });

  it('should call onClick handler when button is clicked', () => {
    const recentDate = new Date();
    recentDate.setDate(recentDate.getDate() - 50);
    
    render(
      <InventoryStatusCell 
        lastStockTaking={recentDate} 
        onClick={mockOnClick} 
      />
    );
    
    fireEvent.click(screen.getByRole('button'));
    expect(mockOnClick).toHaveBeenCalledTimes(1);
  });

  it('should have proper tooltip text', () => {
    render(
      <InventoryStatusCell 
        lastStockTaking={null} 
        onClick={mockOnClick} 
      />
    );
    
    expect(screen.getByRole('button')).toHaveAttribute(
      'title', 
      'Klikněte pro inventarizaci materiálu'
    );
  });

  it('should handle edge case at 120 days boundary (should be orange)', () => {
    const boundaryDate = new Date();
    boundaryDate.setDate(boundaryDate.getDate() - 120); // Exactly 120 days ago
    
    render(
      <InventoryStatusCell 
        lastStockTaking={boundaryDate} 
        onClick={mockOnClick} 
      />
    );
    
    // Check for the actual text content that gets rendered (should be around 120-121 days)
    expect(screen.getByText(/12[01] d/)).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-orange-500');
  });

  it('should handle edge case at 250 days boundary (should be orange)', () => {
    const boundaryDate = new Date();
    boundaryDate.setDate(boundaryDate.getDate() - 250); // Exactly 250 days ago
    
    render(
      <InventoryStatusCell 
        lastStockTaking={boundaryDate} 
        onClick={mockOnClick} 
      />
    );
    
    // Check for the actual text content that gets rendered (should be around 250-251 days)
    expect(screen.getByText(/25[01] d/)).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-orange-500');
  });

  it('should handle edge case at 251 days (should be red)', () => {
    const boundaryDate = new Date();
    boundaryDate.setDate(boundaryDate.getDate() - 251); // 251 days ago
    
    render(
      <InventoryStatusCell 
        lastStockTaking={boundaryDate} 
        onClick={mockOnClick} 
      />
    );
    
    expect(screen.getByText('251 d')).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveClass('bg-red-500');
  });
});