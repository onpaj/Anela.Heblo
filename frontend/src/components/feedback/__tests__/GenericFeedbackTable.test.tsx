import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import GenericFeedbackTable from '../GenericFeedbackTable';
import type { FeedbackRow } from '../types';

const rows: FeedbackRow[] = [
  {
    id: 'row-1',
    primaryText: 'Jak funguje věrnostní program?',
    secondaryText: 'Věrnostní program nabízí slevy.',
    createdAt: '2026-01-15T10:30:00Z',
    userName: 'Alice Example',
    userId: 'oid-1',
    precisionScore: 4,
    styleScore: null,
    hasFeedback: true,
  },
  {
    id: 'row-2',
    primaryText: 'Co jsou produktové kategorie?',
    createdAt: '2026-01-16T08:00:00Z',
    userId: 'oid-2',
    precisionScore: null,
    styleScore: null,
    hasFeedback: false,
  },
];

const defaultProps = {
  rows,
  isLoading: false,
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1,
  onPageChange: jest.fn(),
  onRowClick: jest.fn(),
  primaryLabel: 'Dotaz',
};

beforeEach(() => jest.clearAllMocks());

test('renders primary text column header', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Dotaz')).toBeInTheDocument();
});

test('renders rows with primary text', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Jak funguje věrnostní program?')).toBeInTheDocument();
  expect(screen.getByText('Co jsou produktové kategorie?')).toBeInTheDocument();
});

test('renders user column header', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Uživatel')).toBeInTheDocument();
});

test('renders resolved userName, falling back to userId', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Alice Example')).toBeInTheDocument(); // row-1 has a name
  expect(screen.getByText('oid-2')).toBeInTheDocument();         // row-2 falls back to id
});

test('renders dash when row has no user', () => {
  const rowsNoUser: FeedbackRow[] = [
    {
      id: 'r',
      primaryText: 'x',
      createdAt: '2026-01-15T10:30:00Z',
      precisionScore: 3,
      styleScore: 2,
      hasFeedback: true,
    },
  ];
  render(<GenericFeedbackTable {...defaultProps} rows={rowsNoUser} totalCount={1} />);
  // scores are present, so the only dash rendered is the empty user cell
  expect(screen.getByText('–')).toBeInTheDocument();
});

test('shows "Ano" badge for rows with feedback', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Ano')).toBeInTheDocument();
});

test('calls onRowClick with id when row clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  fireEvent.click(screen.getByText('Jak funguje věrnostní program?'));
  expect(defaultProps.onRowClick).toHaveBeenCalledWith('row-1');
});

test('shows empty state when no rows', () => {
  render(<GenericFeedbackTable {...defaultProps} rows={[]} totalCount={0} />);
  expect(screen.getByText('Žádné záznamy nenalezeny.')).toBeInTheDocument();
});

test('disables previous button on first page', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={1} totalPages={3} />);
  const prevButtons = screen.getAllByRole('button').filter(b => b.textContent === '‹');
  expect(prevButtons[0]).toBeDisabled();
});

test('disables next button on last page', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={3} totalPages={3} totalCount={60} />);
  const nextButtons = screen.getAllByRole('button').filter(b => b.textContent === '›');
  expect(nextButtons[0]).toBeDisabled();
});

test('calls onPageChange when next button clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={1} totalPages={3} totalCount={60} />);
  const nextButtons = screen.getAllByRole('button').filter(b => b.textContent === '›');
  fireEvent.click(nextButtons[0]);
  expect(defaultProps.onPageChange).toHaveBeenCalledWith(2);
});

test('shows loading text when isLoading is true', () => {
  render(<GenericFeedbackTable {...defaultProps} isLoading={true} />);
  expect(screen.getByText('Načítám…')).toBeInTheDocument();
});

test('shows "Ne" badge for rows without feedback', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Ne')).toBeInTheDocument();
});

test('calls onPageChange(1) when first button clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={2} totalPages={3} totalCount={60} />);
  const firstButtons = screen.getAllByRole('button').filter(b => b.textContent === '«');
  fireEvent.click(firstButtons[0]);
  expect(defaultProps.onPageChange).toHaveBeenCalledWith(1);
});

test('calls onPageChange(pageNumber - 1) when previous button clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={2} totalPages={3} totalCount={60} />);
  const prevButtons = screen.getAllByRole('button').filter(b => b.textContent === '‹');
  fireEvent.click(prevButtons[0]);
  expect(defaultProps.onPageChange).toHaveBeenCalledWith(1);
});

test('calls onPageChange(totalPages) when last button clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={1} totalPages={3} totalCount={60} />);
  const lastButtons = screen.getAllByRole('button').filter(b => b.textContent === '»');
  fireEvent.click(lastButtons[0]);
  expect(defaultProps.onPageChange).toHaveBeenCalledWith(3);
});
