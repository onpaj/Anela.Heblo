import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import DraftReplySourcesModal from '../DraftReplySourcesModal';
import type { DraftReplySource } from '../hooks/useGenerateDraftReply';

jest.mock('../../../knowledge-base/ChunkDetailModal', () => ({
  __esModule: true,
  default: ({ chunkId, onClose }: { chunkId: string; onClose: () => void }) => (
    <div data-testid="chunk-detail-modal" data-chunk-id={chunkId}>
      <button onClick={onClose}>Zavřít detail</button>
    </div>
  ),
}));

const sources: DraftReplySource[] = [
  {
    chunkId: 'chunk-1',
    documentId: 'd1',
    filename: 'reklamace.pdf',
    excerpt: 'Reklamaci lze uplatnit do 14 dnů.',
    score: 0.9,
  },
  {
    chunkId: 'chunk-2',
    documentId: 'd2',
    filename: 'doprava.pdf',
    excerpt: 'Dopravujeme do 24 hodin.',
    score: 0.8,
  },
];

describe('DraftReplySourcesModal', () => {
  it('renders all source filenames', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    expect(screen.getByText('reklamace.pdf')).toBeInTheDocument();
    expect(screen.getByText('doprava.pdf')).toBeInTheDocument();
  });

  it('renders score as percentage for each source', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    expect(screen.getByText('90%')).toBeInTheDocument();
    expect(screen.getByText('80%')).toBeInTheDocument();
  });

  it('renders excerpt text for each source', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    expect(screen.getByText('Reklamaci lze uplatnit do 14 dnů.')).toBeInTheDocument();
    expect(screen.getByText('Dopravujeme do 24 hodin.')).toBeInTheDocument();
  });

  it('opens ChunkDetailModal with correct chunkId when a source row is clicked', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    fireEvent.click(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
    );
    const detail = screen.getByTestId('chunk-detail-modal');
    expect(detail).toBeInTheDocument();
    expect(detail.dataset.chunkId).toBe('chunk-1');
  });

  it('opens ChunkDetailModal when Space is pressed on a source row', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    fireEvent.keyDown(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
      { key: ' ' },
    );
    expect(screen.getByTestId('chunk-detail-modal')).toBeInTheDocument();
  });

  it('returns to source list when ChunkDetailModal onClose is called', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    fireEvent.click(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
    );
    fireEvent.click(screen.getByText('Zavřít detail'));
    expect(screen.getByText('reklamace.pdf')).toBeInTheDocument();
    expect(screen.queryByTestId('chunk-detail-modal')).not.toBeInTheDocument();
  });

  it('calls onClose when the X button is clicked', () => {
    const onClose = jest.fn();
    render(<DraftReplySourcesModal sources={sources} onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: /zavřít/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape key is pressed on the source list', () => {
    const onClose = jest.fn();
    render(<DraftReplySourcesModal sources={sources} onClose={onClose} />);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does not call onClose on Escape when ChunkDetailModal is open', () => {
    const onClose = jest.fn();
    render(<DraftReplySourcesModal sources={sources} onClose={onClose} />);
    fireEvent.click(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
    );
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
  });
});
