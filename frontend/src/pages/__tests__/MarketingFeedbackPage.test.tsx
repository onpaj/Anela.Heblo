import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import MarketingFeedbackPage from '../MarketingFeedbackPage';
import * as kbHooks from '../../api/hooks/useKnowledgeBase';
import * as marketingWriterHooks from '../../api/hooks/useMarketingWriterPermission';
import * as kbAdapter from '../../components/feedback/adapters/useKbFeedbackAdapter';
import * as leafletAdapter from '../../components/feedback/adapters/useLeafletFeedbackAdapter';
import * as articleAdapter from '../../components/feedback/adapters/useArticleFeedbackAdapter';
import type { FeedbackDetail, GenericFeedbackStats } from '../../components/feedback/types';

jest.mock('../../api/hooks/useKnowledgeBase');
jest.mock('../../api/hooks/useMarketingWriterPermission');
jest.mock('../../components/feedback/adapters/useKbFeedbackAdapter');
jest.mock('../../components/feedback/adapters/useLeafletFeedbackAdapter');
jest.mock('../../components/feedback/adapters/useArticleFeedbackAdapter');

const emptyAdapterResult = {
  rows: [] as FeedbackDetail[],
  stats: undefined as GenericFeedbackStats | undefined,
  totalCount: 0,
  totalPages: 1,
  pageNumber: 1,
  isLoading: false,
  isError: false,
};

const kbRow: FeedbackDetail = {
  id: 'kb-1',
  primaryText: 'KB otázka',
  createdAt: '2026-01-01T00:00:00Z',
  hasFeedback: false,
};

const leafletRow: FeedbackDetail = {
  id: 'lf-1',
  primaryText: 'Leaflet téma',
  createdAt: '2026-01-01T00:00:00Z',
  hasFeedback: false,
};

function setupMocks({
  hasKb = true,
  hasGenAi = false,
}: { hasKb?: boolean; hasGenAi?: boolean } = {}) {
  jest.spyOn(kbHooks, 'useKnowledgeBaseUploadPermission').mockReturnValue(hasKb);
  jest.spyOn(marketingWriterHooks, 'useMarketingWriterPermission').mockReturnValue(hasGenAi);

  jest.spyOn(kbAdapter, 'useKbFeedbackAdapter').mockReturnValue({
    ...emptyAdapterResult,
    rows: [kbRow],
  });
  jest.spyOn(leafletAdapter, 'useLeafletFeedbackAdapter').mockReturnValue({
    ...emptyAdapterResult,
    rows: [leafletRow],
  });
  jest.spyOn(articleAdapter, 'useArticleFeedbackAdapter').mockReturnValue(emptyAdapterResult);
}

beforeEach(() => jest.clearAllMocks());

test('renders three tab buttons', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  expect(screen.getByRole('button', { name: /poradenství/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /letáky/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /články/i })).toBeInTheDocument();
});

test('shows KB rows by default on first tab', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  expect(screen.getByText('KB otázka')).toBeInTheDocument();
});

test('switching to Letáky tab shows leaflet rows', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByRole('button', { name: /letáky/i }));
  expect(screen.getByText('Leaflet téma')).toBeInTheDocument();
});

test('switching tabs resets selected row', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  // Click a KB row to select it
  fireEvent.click(screen.getByText('KB otázka'));
  expect(screen.getByText('Detail záznamu')).toBeInTheDocument();
  // Switch tab — modal should close
  fireEvent.click(screen.getByRole('button', { name: /letáky/i }));
  expect(screen.queryByText('Detail záznamu')).not.toBeInTheDocument();
});

test('allows access when user has only GenAI role', () => {
  setupMocks({ hasKb: false, hasGenAi: true });
  render(<MarketingFeedbackPage />);
  expect(screen.getByRole('button', { name: /poradenství/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /letáky/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /články/i })).toBeInTheDocument();
  expect(screen.queryByText('Přístup odepřen.')).not.toBeInTheDocument();
});

test('shows access denied when user has no roles', () => {
  setupMocks({ hasKb: false, hasGenAi: false });
  render(<MarketingFeedbackPage />);
  expect(screen.getByText('Přístup odepřen.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /poradenství/i })).not.toBeInTheDocument();
});

test('clicking row opens detail modal', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByText('KB otázka'));
  expect(screen.getByText('Detail záznamu')).toBeInTheDocument();
});

test('clicking same row again closes detail modal', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByText('KB otázka'));
  expect(screen.getByText('Detail záznamu')).toBeInTheDocument();
  fireEvent.click(screen.getAllByText('KB otázka')[0]);
  expect(screen.queryByText('Detail záznamu')).not.toBeInTheDocument();
});
