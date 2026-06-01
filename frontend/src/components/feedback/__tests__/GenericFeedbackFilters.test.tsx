import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import GenericFeedbackFilters from '../GenericFeedbackFilters';

const sortColumns = [
  { value: 'CreatedAt', label: 'Datum' },
  { value: 'PrecisionScore', label: 'Přesnost' },
];

const defaultProps = {
  hasFeedback: undefined as boolean | undefined,
  sortBy: 'CreatedAt',
  sortDescending: true,
  pageSize: 20,
  allowedSortColumns: sortColumns,
  onHasFeedbackChange: jest.fn(),
  onSortByChange: jest.fn(),
  onSortDescendingChange: jest.fn(),
  onPageSizeChange: jest.fn(),
};

beforeEach(() => jest.clearAllMocks());

test('renders all four filter selects', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  expect(screen.getByLabelText(/feedback/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/řadit/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/pořadí/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/na stránce/i)).toBeInTheDocument();
});

test('calls onHasFeedbackChange with true when "Pouze s feedbackem" selected', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  fireEvent.change(screen.getByLabelText(/feedback/i), { target: { value: 'true' } });
  expect(defaultProps.onHasFeedbackChange).toHaveBeenCalledWith(true);
});

test('calls onHasFeedbackChange with undefined when "Vše" selected', () => {
  render(<GenericFeedbackFilters {...{ ...defaultProps, hasFeedback: true }} />);
  fireEvent.change(screen.getByLabelText(/feedback/i), { target: { value: '' } });
  expect(defaultProps.onHasFeedbackChange).toHaveBeenCalledWith(undefined);
});

test('calls onSortByChange when sort column changes', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  fireEvent.change(screen.getByLabelText(/řadit/i), { target: { value: 'PrecisionScore' } });
  expect(defaultProps.onSortByChange).toHaveBeenCalledWith('PrecisionScore');
});

test('calls onPageSizeChange with number when page size changes', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  fireEvent.change(screen.getByLabelText(/na stránce/i), { target: { value: '50' } });
  expect(defaultProps.onPageSizeChange).toHaveBeenCalledWith(50);
});
