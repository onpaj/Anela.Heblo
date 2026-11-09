import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import KnowledgeBaseDocumentsTab from '../KnowledgeBaseDocumentsTab';
import {
  useKnowledgeBaseDocumentsQuery,
  useKnowledgeBaseContentTypesQuery,
  useDeleteKnowledgeBaseDocumentMutation,
} from '../../../api/hooks/useKnowledgeBase';

jest.mock('../../../api/hooks/useKnowledgeBase', () => ({
  useKnowledgeBaseDocumentsQuery: jest.fn(),
  useKnowledgeBaseContentTypesQuery: jest.fn(),
  useDeleteKnowledgeBaseDocumentMutation: jest.fn(),
}));

jest.mock('../ChunkDetailModal', () => ({
  __esModule: true,
  default: ({ chunkId }: { chunkId: string }) => <div data-testid="chunk-modal">{chunkId}</div>,
}));

const mockQuery = useKnowledgeBaseDocumentsQuery as jest.Mock;
const mockContentTypes = useKnowledgeBaseContentTypesQuery as jest.Mock;
const mockDelete = useDeleteKnowledgeBaseDocumentMutation as jest.Mock;

const documents = [
  {
    id: 'd1',
    filename: 'a.pdf',
    status: 'indexed',
    contentType: 'application/pdf',
    createdAt: new Date('2026-01-02'),
    indexedAt: new Date('2026-01-03'),
    firstChunkId: 'c1',
  },
  {
    id: 'd2',
    filename: 'b.txt',
    status: 'failed',
    contentType: 'text/plain',
    createdAt: new Date('2026-01-04'),
    indexedAt: undefined,
    firstChunkId: undefined,
  },
];

const lastQueryArgs = () => mockQuery.mock.calls[mockQuery.mock.calls.length - 1][0];

const LocationProbe = () => {
  const location = useLocation();
  return <div data-testid="search">{location.search}</div>;
};

const NavButtons = () => {
  const navigate = useNavigate();
  return (
    <div>
      <button onClick={() => navigate('/?kbPage=3')}>nav-page</button>
      <button onClick={() => navigate('/?kbPageSize=50')}>nav-pagesize</button>
      <button onClick={() => navigate('/?kbSortBy=Filename')}>nav-sortby</button>
      <button onClick={() => navigate('/?kbSortDesc=false')}>nav-sortdesc</button>
      <button onClick={() => navigate('/?kbFilename=abc')}>nav-filename</button>
      <button onClick={() => navigate('/?kbStatus=failed')}>nav-status</button>
      <button onClick={() => navigate('/?kbContentType=text%2Fplain')}>nav-ctype</button>
      <button onClick={() => navigate(-1)}>nav-back</button>
    </div>
  );
};

const renderTab = (url = '/', canDelete = false) =>
  render(
    <MemoryRouter initialEntries={[url]}>
      <KnowledgeBaseDocumentsTab canDelete={canDelete} />
      <LocationProbe />
      <NavButtons />
    </MemoryRouter>,
  );

const search = () => screen.getByTestId('search').textContent;

describe('KnowledgeBaseDocumentsTab URL sync', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockQuery.mockReturnValue({
      data: { documents, totalPages: 10, totalCount: 200 },
      isLoading: false,
      error: null,
    });
    mockContentTypes.mockReturnValue({
      data: { contentTypes: ['application/pdf', 'text/plain'] },
    });
    mockDelete.mockReturnValue({ mutateAsync: jest.fn().mockResolvedValue(undefined) });
  });

  describe('initial state', () => {
    it('uses defaults for an empty URL', () => {
      renderTab('/');
      expect(lastQueryArgs()).toEqual({
        pageNumber: 1,
        pageSize: 20,
        sortBy: 'CreatedAt',
        sortDescending: true,
        filenameFilter: undefined,
        statusFilter: undefined,
        contentTypeFilter: undefined,
      });
      expect(search()).toBe('');
    });

    it('reads all seven params from the URL', () => {
      renderTab(
        '/?kbPage=4&kbPageSize=50&kbSortBy=Filename&kbSortDesc=false&kbFilename=abc&kbStatus=failed&kbContentType=text%2Fplain',
      );
      expect(lastQueryArgs()).toEqual({
        pageNumber: 4,
        pageSize: 50,
        sortBy: 'Filename',
        sortDescending: false,
        filenameFilter: 'abc',
        statusFilter: 'failed',
        contentTypeFilter: 'text/plain',
      });
    });

    it('falls back to default for invalid pageSize', () => {
      renderTab('/?kbPageSize=7');
      expect(lastQueryArgs().pageSize).toBe(20);
    });

    it.each(['0', '-5'])('clamps kbPage=%s to 1', (v) => {
      renderTab(`/?kbPage=${v}`);
      expect(lastQueryArgs().pageNumber).toBe(1);
    });

    it('treats kbSortDesc=false as ascending', () => {
      renderTab('/?kbSortDesc=false');
      expect(lastQueryArgs().sortDescending).toBe(false);
    });
  });

  describe('URL -> state', () => {
    it.each([
      ['nav-page', { pageNumber: 3 }],
      ['nav-pagesize', { pageSize: 50 }],
      ['nav-sortby', { sortBy: 'Filename' }],
      ['nav-sortdesc', { sortDescending: false }],
      ['nav-filename', { filenameFilter: 'abc' }],
      ['nav-status', { statusFilter: 'failed' }],
      ['nav-ctype', { contentTypeFilter: 'text/plain' }],
    ])('%s updates the query args', async (button, expected) => {
      renderTab('/');
      fireEvent.click(screen.getByText(button));
      await waitFor(() => expect(lastQueryArgs()).toMatchObject(expected));
    });

    it('updates the filename input value', async () => {
      renderTab('/');
      fireEvent.click(screen.getByText('nav-filename'));
      await waitFor(() =>
        expect(screen.getByPlaceholderText('Název souboru...')).toHaveValue('abc'),
      );
    });

    it('restores state on navigate(-1)', async () => {
      renderTab('/');
      fireEvent.click(screen.getByText('nav-page'));
      await waitFor(() => expect(lastQueryArgs().pageNumber).toBe(3));
      fireEvent.click(screen.getByText('nav-back'));
      await waitFor(() => expect(lastQueryArgs().pageNumber).toBe(1));
    });
  });

  describe('state -> URL', () => {
    it('writes status select to URL', async () => {
      renderTab('/');
      fireEvent.change(screen.getByDisplayValue('Vše (stav)'), { target: { value: 'failed' } });
      await waitFor(() => expect(search()).toContain('kbStatus=failed'));
    });

    it('writes content-type select to URL', async () => {
      renderTab('/');
      fireEvent.change(screen.getByDisplayValue('Vše (typ)'), {
        target: { value: 'text/plain' },
      });
      await waitFor(() => expect(search()).toContain('kbContentType=text%2Fplain'));
    });

    it('applies filename filter via button', async () => {
      renderTab('/');
      fireEvent.change(screen.getByPlaceholderText('Název souboru...'), {
        target: { value: 'abc' },
      });
      fireEvent.click(screen.getByText('Filtrovat'));
      await waitFor(() => expect(search()).toContain('kbFilename=abc'));
    });

    it('applies filename filter via Enter', async () => {
      renderTab('/');
      const input = screen.getByPlaceholderText('Název souboru...');
      fireEvent.change(input, { target: { value: 'xyz' } });
      fireEvent.keyDown(input, { key: 'Enter' });
      await waitFor(() => expect(search()).toContain('kbFilename=xyz'));
    });

    it('writes page size change to URL and resets page', async () => {
      renderTab('/?kbPage=3');
      fireEvent.change(screen.getByDisplayValue('20'), { target: { value: '50' } });
      await waitFor(() => expect(search()).toContain('kbPageSize=50'));
      expect(search()).not.toContain('kbPage=');
    });

    it('writes page click to URL', async () => {
      renderTab('/');
      fireEvent.click(screen.getByRole('button', { name: '2' }));
      await waitFor(() => expect(search()).toContain('kbPage=2'));
    });

    it('clear filters removes params', async () => {
      renderTab('/?kbStatus=failed&kbFilename=abc&kbContentType=text%2Fplain&kbSortBy=Filename');
      fireEvent.click(screen.getByText('Vymazat'));
      await waitFor(() => expect(search()).toBe(''));
    });

    it('defaults leave search empty', async () => {
      renderTab('/');
      await waitFor(() => expect(search()).toBe(''));
    });
  });
});
