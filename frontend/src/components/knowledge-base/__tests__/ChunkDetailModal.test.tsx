import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ChunkDetailModal from '../ChunkDetailModal';
import * as hooks from '../../../api/hooks/useKnowledgeBase';

const mockChunkDetail = {
  success: true,
  chunkId: 'chunk-1',
  documentId: 'doc-1',
  filename: 'conversation-2024.txt',
  documentType: 'Conversation' as const,
  indexedAt: '2024-03-15T10:00:00Z',
  chunkIndex: 0,
  summary: 'This is an AI-generated summary of the conversation.',
  content: 'This is the full conversation text that can be very long.',
};

const mockOnClose = jest.fn();

function renderModal(chunkId = 'chunk-1', score?: number) {
  return render(
    <ChunkDetailModal chunkId={chunkId} score={score} onClose={mockOnClose} />
  );
}

beforeEach(() => {
  jest.clearAllMocks();
});

test('renders loading skeleton while fetching', () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: undefined,
    isLoading: true,
    isError: false,
  } as any);

  renderModal();
  expect(screen.getByRole('dialog')).toBeInTheDocument();
  // Loading state — no content yet
  expect(screen.queryByText('Shrnutí')).not.toBeInTheDocument();
});

test('renders summary and content when loaded', async () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: mockChunkDetail,
    isLoading: false,
    isError: false,
  } as any);

  renderModal('chunk-1', 0.87);

  expect(screen.getByText('conversation-2024.txt')).toBeInTheDocument();
  expect(screen.getByText('Shrnutí')).toBeInTheDocument();
  expect(screen.getByText('This is an AI-generated summary of the conversation.')).toBeInTheDocument();
  expect(screen.getByText('Obsah')).toBeInTheDocument();
  expect(screen.getByText('This is the full conversation text that can be very long.')).toBeInTheDocument();
  expect(screen.getByText('87%')).toBeInTheDocument();
});

test('calls onClose when X button clicked', () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: mockChunkDetail,
    isLoading: false,
    isError: false,
  } as any);

  renderModal();
  fireEvent.click(screen.getByLabelText('Zavřít'));
  expect(mockOnClose).toHaveBeenCalledTimes(1);
});

test('calls onClose on Escape key', () => {
  jest.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
    data: mockChunkDetail,
    isLoading: false,
    isError: false,
  } as any);

  renderModal();
  fireEvent.keyDown(document, { key: 'Escape' });
  expect(mockOnClose).toHaveBeenCalledTimes(1);
});
