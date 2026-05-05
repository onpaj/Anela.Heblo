import React from 'react';
import { render, screen } from '@testing-library/react';
import LeafletChunkDetailModal from '../LeafletChunkDetailModal';
import * as hooks from '../../../api/hooks/useLeaflet';

const mockChunkDetail = {
  success: true,
  chunkId: 'chunk-1',
  documentId: 'doc-1',
  filename: 'leaflet.pdf',
  documentType: 'Document',
  indexedAt: '2024-03-15T10:00:00Z',
  chunkIndex: 0,
  summary: 'Summary text.',
  content: 'Full leaflet content.',
};

const mockOnClose = jest.fn();

function renderModal() {
  return render(
    <LeafletChunkDetailModal chunkId="chunk-1" onClose={mockOnClose} />
  );
}

beforeEach(() => {
  jest.clearAllMocks();
});

test('renders SharePoint link when sourcePath is an https URL', () => {
  jest.spyOn(hooks, 'useLeafletChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: 'https://anelacz.sharepoint.com/sites/x/leaflet.pdf' },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  const link = screen.getByRole('link', { name: /Otevřít v SharePoint/i });
  expect(link).toHaveAttribute('href', 'https://anelacz.sharepoint.com/sites/x/leaflet.pdf');
  expect(link).toHaveAttribute('target', '_blank');
  expect(link).toHaveAttribute('rel', 'noopener noreferrer');
});

test('hides SharePoint link when sourcePath is a synthetic upload path', () => {
  jest.spyOn(hooks, 'useLeafletChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: 'upload/abc/file.pdf' },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  expect(screen.queryByRole('link', { name: /Otevřít v SharePoint/i })).not.toBeInTheDocument();
});

test('hides SharePoint link when sourcePath is missing', () => {
  jest.spyOn(hooks, 'useLeafletChunkDetailQuery').mockReturnValue({
    data: { ...mockChunkDetail, sourcePath: undefined },
    isLoading: false,
    isError: false,
  } as any);

  renderModal();

  expect(screen.queryByRole('link', { name: /Otevřít v SharePoint/i })).not.toBeInTheDocument();
});
