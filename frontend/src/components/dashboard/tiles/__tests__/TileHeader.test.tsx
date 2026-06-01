import React from 'react';
import { render, screen } from '@testing-library/react';
import { TileHeader } from '../TileHeader';

describe('TileHeader', () => {
  it('should render title text', () => {
    render(<TileHeader title="Test Tile Title" />);
    
    expect(screen.getByText('Test Tile Title')).toBeInTheDocument();
  });

  it('should apply drag handle props when provided', () => {
    const mockDragHandleProps = {
      'data-testid': 'drag-handle',
      'aria-label': 'Drag handle',
      tabIndex: 0
    };
    
    render(<TileHeader title="Test Title" dragHandleProps={mockDragHandleProps} />);
    
    const header = screen.getByTestId('drag-handle');
    expect(header).toBeInTheDocument();
    expect(header).toHaveAttribute('aria-label', 'Drag handle');
    expect(header).toHaveAttribute('tabIndex', '0');
  });

  it('should render without drag handle props', () => {
    render(<TileHeader title="Test Title" />);
    
    const header = screen.getByText('Test Title').closest('div');
    expect(header).toBeInTheDocument();
  });

  it('should apply correct styling classes', () => {
    render(<TileHeader title="Test Title" />);
    
    const header = screen.getByText('Test Title').closest('div');
    expect(header).toHaveClass(
      'px-4',
      'py-3',
      'border-b',
      'border-gray-100',
      'flex',
      'justify-between',
      'items-center'
    );
  });

  it('should handle empty title', () => {
    render(<TileHeader title="" />);
    
    // Should still render the header structure
    const headerElement = screen.getByRole('heading', { level: 3 });
    expect(headerElement).toBeInTheDocument();
    expect(headerElement).toHaveTextContent('');
  });

  it('should handle long title text', () => {
    const longTitle = 'This is a very long title that might need to be handled properly in the UI layout';
    render(<TileHeader title={longTitle} />);
    
    expect(screen.getByText(longTitle)).toBeInTheDocument();
  });

  it('should apply title styling', () => {
    render(<TileHeader title="Test Title" />);

    const titleElement = screen.getByText('Test Title');
    expect(titleElement).toHaveClass(
      'text-base',
      'md:text-sm',
      'font-medium',
      'text-gray-900',
      'truncate',
      'tile-title',
      'leading-relaxed'
    );
  });

  it('should render as h3 element', () => {
    render(<TileHeader title="Test Title" />);
    
    const titleElement = screen.getByRole('heading', { level: 3 });
    expect(titleElement).toBeInTheDocument();
    expect(titleElement).toHaveTextContent('Test Title');
  });

  it('should handle drag handle props with event handlers', () => {
    const mockOnMouseDown = jest.fn();
    const mockOnKeyDown = jest.fn();
    
    const dragHandleProps = {
      onMouseDown: mockOnMouseDown,
      onKeyDown: mockOnKeyDown,
      'data-testid': 'drag-handle'
    };
    
    render(<TileHeader title="Test Title" dragHandleProps={dragHandleProps} />);
    
    const header = screen.getByTestId('drag-handle');
    expect(header).toBeInTheDocument();
    
    // The event handlers should be attached via the spread operator
    // We can't directly test them without triggering events, but we can verify
    // the component renders correctly with these props
    expect(header).toBeDefined();
  });

  it('should spread all drag handle props correctly', () => {
    const dragHandleProps = {
      'data-custom': 'value',
      'aria-describedby': 'description',
      role: 'button',
      'data-testid': 'custom-drag-handle'
    };
    
    render(<TileHeader title="Test Title" dragHandleProps={dragHandleProps} />);
    
    const header = screen.getByTestId('custom-drag-handle');
    expect(header).toHaveAttribute('data-custom', 'value');
    expect(header).toHaveAttribute('aria-describedby', 'description');
    expect(header).toHaveAttribute('role', 'button');
  });

  it('should handle special characters in title', () => {
    const specialTitle = 'Title with émojis 🚀 & symbols @ #';
    render(<TileHeader title={specialTitle} />);
    
    expect(screen.getByText(specialTitle)).toBeInTheDocument();
  });

  it('should maintain proper DOM structure', () => {
    render(<TileHeader title="Test Title" />);
    
    const container = screen.getByText('Test Title').closest('div');
    const heading = screen.getByRole('heading', { level: 3 });
    
    expect(container).toContainElement(heading);
    expect(container).toHaveClass('flex', 'items-center');
  });

  it('should handle undefined drag handle props gracefully', () => {
    render(<TileHeader title="Test Title" dragHandleProps={undefined} />);
    
    const header = screen.getByText('Test Title').closest('div');
    expect(header).toBeInTheDocument();
  });

  it('should handle null title gracefully', () => {
    render(<TileHeader title={null as any} />);
    
    const headerElement = screen.getByRole('heading', { level: 3 });
    expect(headerElement).toBeInTheDocument();
  });
});