import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import KnowledgeBaseUploadTab from '../KnowledgeBaseUploadTab';
import { useUploadKnowledgeBaseDocumentMutation } from '../../../api/hooks/useKnowledgeBase';

jest.mock('lucide-react', () => ({
  Upload: () => <svg data-testid="icon-upload" />,
  X: () => <svg data-testid="icon-x" />,
  FileText: () => <svg data-testid="icon-filetext" />,
}));

jest.mock('../../../api/hooks/useKnowledgeBase', () => ({
  useUploadKnowledgeBaseDocumentMutation: jest.fn(),
}));

const mockUseUploadKnowledgeBaseDocumentMutation =
  useUploadKnowledgeBaseDocumentMutation as jest.Mock;

const makeFile = (name: string, type = 'application/pdf'): File =>
  new File(['content'], name, { type });

const simulateDrop = (dropZone: HTMLElement, files: File[]) => {
  const dataTransfer = {
    files: Object.assign(files, {
      item: (i: number) => files[i],
      length: files.length,
    }),
  };
  fireEvent.drop(dropZone, { dataTransfer });
};

const getDropZone = () => screen.getByTestId('drop-zone');

describe('KnowledgeBaseUploadTab', () => {
  let mockMutateAsync: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockMutateAsync = jest.fn().mockResolvedValue({ success: true, document: null });
    mockUseUploadKnowledgeBaseDocumentMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
    });
  });

  it('drops 3 valid files and all appear in the list with "Čeká" status', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    const files = [
      makeFile('document1.pdf'),
      makeFile('report.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'),
      makeFile('notes.txt', 'text/plain'),
    ];
    simulateDrop(dropZone, files);
    expect(screen.getByText('document1.pdf')).toBeInTheDocument();
    expect(screen.getByText('report.docx')).toBeInTheDocument();
    expect(screen.getByText('notes.txt')).toBeInTheDocument();
    const waitingStatuses = screen.getAllByText('Čeká');
    expect(waitingStatuses).toHaveLength(3);
  });

  it('does not add a file with unsupported extension (.exe) to the queue', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('virus.exe', 'application/octet-stream')]);
    expect(screen.queryByText('virus.exe')).not.toBeInTheDocument();
  });

  it('accepts a file with uppercase extension (.PDF)', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('DOCUMENT.PDF')]);
    expect(screen.getByText('DOCUMENT.PDF')).toBeInTheDocument();
  });

  it('ignores a duplicate filename on second drop', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('file.pdf')]);
    simulateDrop(dropZone, [makeFile('file.pdf')]);
    expect(screen.getAllByText('file.pdf')).toHaveLength(1);
  });

  it('removes a file from the queue when the X button is clicked', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('removeme.pdf'), makeFile('keepme.txt', 'text/plain')]);
    const removeButtons = screen.getAllByLabelText('Odebrat');
    fireEvent.click(removeButtons[0]);
    expect(screen.queryByText('removeme.pdf')).not.toBeInTheDocument();
    expect(screen.getByText('keepme.txt')).toBeInTheDocument();
  });

  it('uploads with correct documentType passed to mutation', async () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('notes.txt', 'text/plain')]);

    fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          documentType: 'Conversation',
        })
      );
    });
  });

  it('defaults .pdf to KnowledgeBase document type', async () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('manual.pdf', 'application/pdf')]);

    fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          documentType: 'KnowledgeBase',
        })
      );
    });
  });

  it('shows document type selector per file', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('notes.txt', 'text/plain'), makeFile('doc.pdf')]);
    const selects = screen.getAllByRole('combobox');
    expect(selects).toHaveLength(2);
  });

  it('allows overriding document type before upload', async () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('manual.pdf', 'application/pdf')]);

    // Default is KnowledgeBase for pdf — override to Conversation
    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'Conversation' } });

    fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ documentType: 'Conversation' })
      );
    });
  });

  it('uploads all files successfully and removes done files', async () => {
    mockMutateAsync.mockResolvedValue({ success: true });
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('file1.pdf'), makeFile('file2.txt', 'text/plain')]);
    fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    await waitFor(() => {
      expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(screen.queryByText('file2.txt')).not.toBeInTheDocument();
    });
  });

  it('shows error status for failed file', async () => {
    mockMutateAsync
      .mockResolvedValueOnce({ success: true })
      .mockRejectedValueOnce(new Error('Upload failed'));
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('success.pdf'), makeFile('failure.txt', 'text/plain')]);
    fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    await waitFor(() => { expect(screen.getByText('❌ Chyba')).toBeInTheDocument(); });
    expect(screen.getByText('failure.txt')).toBeInTheDocument();
  });

  it('clears the queue when "Zrušit vše" is clicked', () => {
    render(<KnowledgeBaseUploadTab />);
    const dropZone = getDropZone();
    simulateDrop(dropZone, [makeFile('file1.pdf')]);
    fireEvent.click(screen.getByRole('button', { name: /Zrušit vše/i }));
    expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
  });

  it('renders without any props', () => {
    expect(() => { render(<KnowledgeBaseUploadTab />); }).not.toThrow();
    expect(screen.getByText('Přetáhněte soubory sem')).toBeInTheDocument();
  });
});
