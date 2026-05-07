import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ArticleSourceList from '../ArticleSourceList';
import { ArticleSource } from '../../../api/hooks/useArticles';

jest.mock('../../../components/knowledge-base/ChunkDetailModal', () => ({
  __esModule: true,
  default: ({ chunkId, onClose }: { chunkId: string; onClose: () => void }) => (
    <div data-testid="chunk-modal" data-chunk-id={chunkId}>
      <button onClick={onClose}>close-modal</button>
    </div>
  ),
}));

const webSource: ArticleSource = {
  title: 'Web title',
  url: 'https://example.com',
  type: 'Web',
  knowledgeBaseChunkId: null,
  confidence: null,
  excerpt: null,
  validationNote: null,
};

const kbSource: ArticleSource = {
  title: 'KB title',
  url: null,
  type: 'KnowledgeBase',
  knowledgeBaseChunkId: '11111111-1111-1111-1111-111111111111',
  confidence: 0.9,
  excerpt: 'ex',
  validationNote: 'ok',
};

const orphanKbSource: ArticleSource = {
  title: 'Orphan KB',
  url: null,
  type: 'KnowledgeBase',
  knowledgeBaseChunkId: null,
  confidence: null,
  excerpt: null,
  validationNote: null,
};

describe('ArticleSourceList', () => {
  it('renders web sources as anchor tags', () => {
    render(<ArticleSourceList sources={[webSource]} />);
    const link = screen.getByRole('link', { name: /Web title/ });
    expect(link).toHaveAttribute('href', 'https://example.com');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  });

  it('renders KB sources with a chunkId as buttons that open the chunk modal', () => {
    render(<ArticleSourceList sources={[kbSource]} />);
    expect(screen.queryByTestId('chunk-modal')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /KB title/ }));

    const modal = screen.getByTestId('chunk-modal');
    expect(modal).toBeInTheDocument();
    expect(modal).toHaveAttribute('data-chunk-id', kbSource.knowledgeBaseChunkId as string);
  });

  it('clears modal state when modal closes', () => {
    render(<ArticleSourceList sources={[kbSource]} />);
    fireEvent.click(screen.getByRole('button', { name: /KB title/ }));
    fireEvent.click(screen.getByText('close-modal'));
    expect(screen.queryByTestId('chunk-modal')).not.toBeInTheDocument();
  });

  it('renders KB sources without chunkId as plain text', () => {
    render(<ArticleSourceList sources={[orphanKbSource]} />);
    expect(screen.getByText('Orphan KB')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Orphan KB/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Orphan KB/ })).not.toBeInTheDocument();
  });

  it('renders nothing when sources are empty', () => {
    const { container } = render(<ArticleSourceList sources={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
