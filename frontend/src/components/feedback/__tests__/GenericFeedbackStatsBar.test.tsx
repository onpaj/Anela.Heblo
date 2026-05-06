import React from 'react';
import { render, screen } from '@testing-library/react';
import GenericFeedbackStatsBar from '../GenericFeedbackStatsBar';
import type { GenericFeedbackStats } from '../types';

const stats: GenericFeedbackStats = {
  totalItems: 42,
  totalWithFeedback: 10,
  avgPrecisionScore: 3.5,
  avgStyleScore: null,
};

test('shows skeleton cards when loading', () => {
  render(<GenericFeedbackStatsBar stats={undefined} isLoading={true} itemLabel="dotazů" />);
  const skeletons = screen.getAllByTestId('skeleton-card');
  expect(skeletons.length).toBe(4);
});

test('shows item count with label', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getByText('42')).toBeInTheDocument();
  expect(screen.getByText(/dotazů/i)).toBeInTheDocument();
});

test('shows feedback count and percentage', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getByText('10')).toBeInTheDocument();
  expect(screen.getByText(/24 %/)).toBeInTheDocument();
});

test('shows precision score when present', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getByText('3.5')).toBeInTheDocument();
});

test('shows dash when style score is null', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getAllByText('–').length).toBeGreaterThan(0);
});
