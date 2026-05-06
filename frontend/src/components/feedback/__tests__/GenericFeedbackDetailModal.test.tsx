import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import GenericFeedbackDetailModal from '../GenericFeedbackDetailModal';
import type { FeedbackDetail } from '../types';

const detail: FeedbackDetail = {
  id: 'log-1',
  primaryText: 'Jak funguje věrnostní program?',
  secondaryText: 'Věrnostní program nabízí slevy zákazníkům.',
  createdAt: '2026-01-15T10:30:00Z',
  userId: 'user@anela.cz',
  precisionScore: 4,
  styleScore: 3,
  hasFeedback: true,
  feedbackComment: 'Odpověď byla přesná.',
};

const onClose = jest.fn();

beforeEach(() => jest.clearAllMocks());

test('renders primary label and text', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('Dotaz')).toBeInTheDocument();
  expect(screen.getByText('Jak funguje věrnostní program?')).toBeInTheDocument();
});

test('renders secondary label and text', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('Odpověď')).toBeInTheDocument();
  expect(screen.getByText('Věrnostní program nabízí slevy zákazníkům.')).toBeInTheDocument();
});

test('renders feedback comment when present', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('Odpověď byla přesná.')).toBeInTheDocument();
});

test('renders userId when present', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('user@anela.cz')).toBeInTheDocument();
});

test('calls onClose when X button clicked', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  fireEvent.click(screen.getByLabelText('Zavřít'));
  expect(onClose).toHaveBeenCalledTimes(1);
});

test('calls onClose on Escape key', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  fireEvent.keyDown(document, { key: 'Escape' });
  expect(onClose).toHaveBeenCalledTimes(1);
});

test('renders extra content when provided', () => {
  render(
    <GenericFeedbackDetailModal
      detail={{ ...detail, extra: <div>TopK: 5</div> }}
      onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('TopK: 5')).toBeInTheDocument();
});

test('hides feedback section when hasFeedback is false', () => {
  render(
    <GenericFeedbackDetailModal
      detail={{ ...detail, hasFeedback: false }}
      onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.queryByText('Feedback')).not.toBeInTheDocument();
  expect(screen.queryByText('Odpověď byla přesná.')).not.toBeInTheDocument();
});

test('hides userId row when userId is absent', () => {
  render(
    <GenericFeedbackDetailModal
      detail={{ ...detail, userId: undefined }}
      onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.queryByText('Uživatel')).not.toBeInTheDocument();
});
