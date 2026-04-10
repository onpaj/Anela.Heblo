import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import Pagination from '../Pagination';

const defaultProps = {
  totalCount: 50,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 3,
  onPageChange: jest.fn(),
  onPageSizeChange: jest.fn(),
};

describe('Pagination', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('returns null when totalCount is 0', () => {
    render(<Pagination {...defaultProps} totalCount={0} totalPages={0} />);
    expect(screen.queryByText('Předchozí')).not.toBeInTheDocument();
    expect(screen.queryByText('Další')).not.toBeInTheDocument();
  });

  it('renders item range "1-20 z 50"', () => {
    render(<Pagination {...defaultProps} />);
    expect(screen.getByText(/1-20 z 50/)).toBeInTheDocument();
  });

  it('shows "(filtrováno)" when isFiltered is true', () => {
    render(<Pagination {...defaultProps} isFiltered={true} />);
    expect(screen.getByText('(filtrováno)')).toBeInTheDocument();
  });

  it('does not show "(filtrováno)" when isFiltered is false', () => {
    render(<Pagination {...defaultProps} isFiltered={false} />);
    expect(screen.queryByText('(filtrováno)')).not.toBeInTheDocument();
  });

  it('does not show "(filtrováno)" when isFiltered is omitted', () => {
    render(<Pagination {...defaultProps} />);
    expect(screen.queryByText('(filtrováno)')).not.toBeInTheDocument();
  });

  it('disables previous button on first page', () => {
    render(<Pagination {...defaultProps} pageNumber={1} />);
    const prevButtons = screen.getAllByText('Předchozí');
    prevButtons.forEach((btn) => {
      expect(btn).toBeDisabled();
    });
  });

  it('disables next button on last page', () => {
    render(<Pagination {...defaultProps} pageNumber={3} totalPages={3} />);
    const nextButtons = screen.getAllByText('Další');
    nextButtons.forEach((btn) => {
      expect(btn).toBeDisabled();
    });
  });

  it('calls onPageChange with correct value when a page button is clicked', () => {
    const onPageChange = jest.fn();
    render(<Pagination {...defaultProps} onPageChange={onPageChange} pageNumber={1} totalPages={3} />);
    // Page buttons rendered for pages 1, 2, 3 - click page 2
    const pageButtons = screen.getAllByRole('button', { name: '2' });
    fireEvent.click(pageButtons[0]);
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it('calls onPageChange with pageNumber - 1 when previous is clicked', () => {
    const onPageChange = jest.fn();
    render(<Pagination {...defaultProps} onPageChange={onPageChange} pageNumber={2} totalPages={3} />);
    const prevButtons = screen.getAllByText('Předchozí');
    fireEvent.click(prevButtons[0]);
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it('calls onPageChange with pageNumber + 1 when next is clicked', () => {
    const onPageChange = jest.fn();
    render(<Pagination {...defaultProps} onPageChange={onPageChange} pageNumber={1} totalPages={3} />);
    const nextButtons = screen.getAllByText('Další');
    fireEvent.click(nextButtons[0]);
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it('calls onPageSizeChange when select value changes', () => {
    const onPageSizeChange = jest.fn();
    render(<Pagination {...defaultProps} onPageSizeChange={onPageSizeChange} />);
    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: '50' } });
    expect(onPageSizeChange).toHaveBeenCalledWith(50);
  });

  it('renders up to 5 page buttons when totalPages >= 5', () => {
    render(
      <Pagination
        {...defaultProps}
        totalCount={100}
        pageNumber={1}
        pageSize={20}
        totalPages={10}
      />,
    );
    // Page buttons are numbered 1-5 for pageNumber=1, totalPages=10
    for (let i = 1; i <= 5; i++) {
      expect(screen.getAllByRole('button', { name: String(i) }).length).toBeGreaterThanOrEqual(1);
    }
    // Page 6 should not be rendered
    expect(screen.queryByRole('button', { name: '6' })).not.toBeInTheDocument();
  });

  it('renders all page buttons when totalPages < 5', () => {
    render(
      <Pagination
        {...defaultProps}
        totalCount={50}
        pageNumber={1}
        pageSize={20}
        totalPages={3}
      />,
    );
    for (let i = 1; i <= 3; i++) {
      expect(screen.getAllByRole('button', { name: String(i) }).length).toBeGreaterThanOrEqual(1);
    }
  });
});
