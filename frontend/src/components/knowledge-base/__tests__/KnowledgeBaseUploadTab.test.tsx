import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import '@testing-library/jest-dom';
import KnowledgeBaseUploadTab from '../KnowledgeBaseUploadTab';
import { useUploadKnowledgeBaseDocumentMutation } from '../../../api/hooks/useKnowledgeBase';

// Mock lucide-react icons as simple svg stubs
jest.mock('lucide-react', () => ({
  Upload: () => <svg data-testid="icon-upload" />,
  X: () => <svg data-testid="icon-x" />,
  FileText: () => <svg data-testid="icon-filetext" />,
}));

// Mock the upload mutation hook
jest.mock('../../../api/hooks/useKnowledgeBase', () => ({
  useUploadKnowledgeBaseDocumentMutation: jest.fn(),
}));

const mockUseUploadKnowledgeBaseDocumentMutation =
  useUploadKnowledgeBaseDocumentMutation as jest.Mock;

// Helper to create a File object
const makeFile = (name: string, type = 'application/pdf'): File => {
  return new File(['content'], name, { type });
};

// Helper to simulate a drop event with the given files
const simulateDrop = (dropZone: HTMLElement, files: File[]) => {
  const dataTransfer = {
    files: Object.assign(files, {
      item: (i: number) => files[i],
      length: files.length,
    }),
  };
  fireEvent.drop(dropZone, { dataTransfer });
};

// Helper to get the drop zone element
const getDropZone = () => {
  // The drop zone has specific text content
  return screen.getByText('Přetáhněte soubory sem').closest('div[class*="border-dashed"]') as HTMLElement;
};

describe('KnowledgeBaseUploadTab', () => {
  let mockMutateAsync: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockMutateAsync = jest.fn().mockResolvedValue({ success: true, document: null });
    mockUseUploadKnowledgeBaseDocumentMutation.mockReturnValue({
      mutateAsync: mockMutateAsync,
    });
  });

  // -------------------------------------------------------------------------
  // Test 1: Drop 3 valid files - all appear with "Čeká" status
  // -------------------------------------------------------------------------
  it('drops 3 valid files and all appear in the list with "Čeká" status', () => {
    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    const files = [
      makeFile('document1.pdf'),
      makeFile('report.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'),
      makeFile('notes.txt', 'text/plain'),
    ];

    act(() => {
      simulateDrop(dropZone, files);
    });

    expect(screen.getByText('document1.pdf')).toBeInTheDocument();
    expect(screen.getByText('report.docx')).toBeInTheDocument();
    expect(screen.getByText('notes.txt')).toBeInTheDocument();

    // All three should show "Čeká" status
    const waitingStatuses = screen.getAllByText('Čeká');
    expect(waitingStatuses).toHaveLength(3);
  });

  // -------------------------------------------------------------------------
  // Test 2: Drop file with unsupported extension (.exe) - not added to queue
  // -------------------------------------------------------------------------
  it('does not add a file with unsupported extension (.exe) to the queue', () => {
    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('virus.exe', 'application/octet-stream')]);
    });

    expect(screen.queryByText('virus.exe')).not.toBeInTheDocument();
    // File list should not appear
    expect(screen.queryByText('Čeká')).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Test 3: Drop file with uppercase extension (.PDF) - accepted
  // -------------------------------------------------------------------------
  it('accepts a file with uppercase extension (.PDF)', () => {
    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('DOCUMENT.PDF')]);
    });

    expect(screen.getByText('DOCUMENT.PDF')).toBeInTheDocument();
    expect(screen.getByText('Čeká')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Test 4: Drop duplicate filename - second drop ignored, list length unchanged
  // -------------------------------------------------------------------------
  it('ignores a duplicate filename on second drop', () => {
    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file.pdf')]);
    });

    expect(screen.getAllByText('file.pdf')).toHaveLength(1);

    act(() => {
      simulateDrop(dropZone, [makeFile('file.pdf')]);
    });

    // Still only one entry
    expect(screen.getAllByText('file.pdf')).toHaveLength(1);
  });

  // -------------------------------------------------------------------------
  // Test 5: Remove file before upload - file removed from list
  // -------------------------------------------------------------------------
  it('removes a file from the queue when the X button is clicked', () => {
    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('removeme.pdf'), makeFile('keepme.txt', 'text/plain')]);
    });

    expect(screen.getByText('removeme.pdf')).toBeInTheDocument();
    expect(screen.getByText('keepme.txt')).toBeInTheDocument();

    // Click the X button for the first file (removeme.pdf)
    const removeButtons = screen.getAllByLabelText('Odebrat');
    fireEvent.click(removeButtons[0]);

    expect(screen.queryByText('removeme.pdf')).not.toBeInTheDocument();
    expect(screen.getByText('keepme.txt')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Test 6: Click "Nahrát vše" - all succeed
  // -------------------------------------------------------------------------
  it('uploads all files successfully, transitions through statuses, and removes done files', async () => {
    // Use controlled promises so we can verify status transitions
    let resolveFirst: () => void;
    let resolveSecond: () => void;
    const firstPromise = new Promise<void>((res) => { resolveFirst = res; });
    const secondPromise = new Promise<void>((res) => { resolveSecond = res; });

    mockMutateAsync
      .mockReturnValueOnce(firstPromise)
      .mockReturnValueOnce(secondPromise);

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file1.pdf'), makeFile('file2.md', 'text/markdown')]);
    });

    // Start upload
    const uploadButton = screen.getByRole('button', { name: /Nahrát vše/i });
    act(() => {
      fireEvent.click(uploadButton);
    });

    // First file should be "uploading"
    await waitFor(() => {
      expect(screen.getByText('Nahrávám…')).toBeInTheDocument();
    });

    // Upload button should be disabled during upload
    expect(uploadButton).toBeDisabled();

    // Resolve first file
    await act(async () => {
      resolveFirst!();
    });

    // Resolve second file
    await act(async () => {
      resolveSecond!();
    });

    // After upload completes, done files should be removed from queue
    await waitFor(() => {
      expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
      expect(screen.queryByText('file2.md')).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // Test 7: Click "Nahrát vše" - one file fails
  // -------------------------------------------------------------------------
  it('shows error status for failed file and keeps it in queue while other files complete', async () => {
    mockMutateAsync
      .mockResolvedValueOnce({ success: true })
      .mockRejectedValueOnce(new Error('Upload failed'));

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [
        makeFile('success.pdf'),
        makeFile('failure.txt', 'text/plain'),
      ]);
    });

    const uploadButton = screen.getByRole('button', { name: /Nahrát vše/i });
    act(() => {
      fireEvent.click(uploadButton);
    });

    // Wait for upload to complete
    await waitFor(() => {
      expect(screen.getByText('❌ Chyba')).toBeInTheDocument();
    });

    // Failed file remains in queue
    expect(screen.getByText('failure.txt')).toBeInTheDocument();

    // Successful file is removed
    await waitFor(() => {
      expect(screen.queryByText('success.pdf')).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // Test 8: Upload again after partial failure - only failed file re-processed
  // -------------------------------------------------------------------------
  it('re-processes only failed files on second upload attempt', async () => {
    mockMutateAsync
      .mockResolvedValueOnce({ success: true })   // file1 succeeds on first run
      .mockRejectedValueOnce(new Error('Failed')) // file2 fails on first run
      .mockResolvedValueOnce({ success: true });  // file2 succeeds on second run

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file1.pdf'), makeFile('file2.txt', 'text/plain')]);
    });

    // First upload
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    });

    await waitFor(() => {
      expect(screen.getByText('❌ Chyba')).toBeInTheDocument();
    });

    // file1.pdf should be gone (done), file2.txt should remain (error)
    expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
    expect(screen.getByText('file2.txt')).toBeInTheDocument();

    // Second upload - should only call mutateAsync once more for the failed file
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    });

    await waitFor(() => {
      // After successful retry, file2.txt is removed
      expect(screen.queryByText('file2.txt')).not.toBeInTheDocument();
    });

    // mutateAsync called 3 times total: file1 + file2 (fail) + file2 (retry)
    expect(mockMutateAsync).toHaveBeenCalledTimes(3);
  });

  // -------------------------------------------------------------------------
  // Test 9: Click "Zrušit vše" - queue cleared, statuses cleared, file list hidden
  // -------------------------------------------------------------------------
  it('clears the queue and hides the file list when "Zrušit vše" is clicked', () => {
    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file1.pdf'), makeFile('file2.txt', 'text/plain')]);
    });

    expect(screen.getByText('file1.pdf')).toBeInTheDocument();
    expect(screen.getByText('file2.txt')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Zrušit vše/i }));

    expect(screen.queryByText('file1.pdf')).not.toBeInTheDocument();
    expect(screen.queryByText('file2.txt')).not.toBeInTheDocument();
    // No "Čeká" status remaining
    expect(screen.queryByText('Čeká')).not.toBeInTheDocument();
    // Upload button row should be gone
    expect(screen.queryByRole('button', { name: /Nahrát vše/i })).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Test 10: Drop zone always visible
  // -------------------------------------------------------------------------
  it('renders the drop zone before, during queue, and after upload', async () => {
    mockMutateAsync.mockResolvedValue({ success: true });

    render(<KnowledgeBaseUploadTab />);

    // Before any files
    expect(screen.getByText('Přetáhněte soubory sem')).toBeInTheDocument();

    // Add files
    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file.pdf')]);
    });

    // While files are queued
    expect(screen.getByText('Přetáhněte soubory sem')).toBeInTheDocument();

    // After upload completes
    fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));

    await waitFor(() => {
      expect(screen.queryByText('file.pdf')).not.toBeInTheDocument();
    });

    // Drop zone still visible
    expect(screen.getByText('Přetáhněte soubory sem')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Test 11: Upload button label shows pending count
  // -------------------------------------------------------------------------
  it('shows correct pending count in upload button label', async () => {
    let resolveFirst: () => void;
    let resolveSecond: () => void;
    let resolveThird: () => void;
    const firstPromise = new Promise<void>((res) => { resolveFirst = res; });
    const secondPromise = new Promise<void>((res) => { resolveSecond = res; });
    const thirdPromise = new Promise<void>((res) => { resolveThird = res; });

    mockMutateAsync
      .mockReturnValueOnce(firstPromise)
      .mockReturnValueOnce(secondPromise)
      .mockReturnValueOnce(thirdPromise);

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [
        makeFile('file1.pdf'),
        makeFile('file2.txt', 'text/plain'),
        makeFile('file3.md', 'text/markdown'),
      ]);
    });

    // All 3 pending before upload starts
    expect(screen.getByRole('button', { name: /Nahrát vše \(3\)/i })).toBeInTheDocument();

    // Start upload
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /Nahrát vše \(3\)/i }));
    });

    // Resolve the first file successfully — it gets removed from queue
    await act(async () => {
      resolveFirst!();
    });

    // After first file completes, only 2 remain pending
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Nahrát vše \(2\)/i })).toBeInTheDocument();
    });

    // Clean up remaining promises
    await act(async () => {
      resolveSecond!();
      resolveThird!();
    });
  });

  // -------------------------------------------------------------------------
  // Test 12: Component renders without any props (no onUploadSuccess prop)
  // -------------------------------------------------------------------------
  it('renders without any props', () => {
    expect(() => {
      render(<KnowledgeBaseUploadTab />);
    }).not.toThrow();

    expect(screen.getByText('Přetáhněte soubory sem')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Test 13: X buttons disabled during upload
  // -------------------------------------------------------------------------
  it('disables all X (remove) buttons while uploading', async () => {
    // Never-resolving promise keeps the component in uploading state
    const neverResolve = new Promise<void>(() => {});
    mockMutateAsync.mockReturnValue(neverResolve);

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file1.pdf'), makeFile('file2.txt', 'text/plain')]);
    });

    // Start upload (don't await — let it hang)
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    });

    // Wait until at least one file is in "uploading" state
    await waitFor(() => {
      expect(screen.getByText('Nahrávám…')).toBeInTheDocument();
    });

    // All remove buttons should be disabled
    const removeButtons = screen.getAllByLabelText('Odebrat');
    removeButtons.forEach((btn) => {
      expect(btn).toBeDisabled();
    });
  });

  // -------------------------------------------------------------------------
  // Test 14: "Zrušit vše" disabled during upload
  // -------------------------------------------------------------------------
  it('disables "Zrušit vše" button while uploading', async () => {
    const neverResolve = new Promise<void>(() => {});
    mockMutateAsync.mockReturnValue(neverResolve);

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('file.pdf')]);
    });

    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    });

    await waitFor(() => {
      expect(screen.getByText('Nahrávám…')).toBeInTheDocument();
    });

    expect(screen.getByRole('button', { name: /Zrušit vše/i })).toBeDisabled();
  });

  // -------------------------------------------------------------------------
  // Test 15: New files dropped during active upload appear in list but not auto-processed
  // -------------------------------------------------------------------------
  it('accepts newly dropped files during an active upload but does not auto-process them', async () => {
    let resolveUpload: () => void;
    const uploadPromise = new Promise<void>((res) => { resolveUpload = res; });
    mockMutateAsync.mockReturnValueOnce(uploadPromise);

    render(<KnowledgeBaseUploadTab />);

    const dropZone = getDropZone();
    act(() => {
      simulateDrop(dropZone, [makeFile('uploading.pdf')]);
    });

    // Start upload
    act(() => {
      fireEvent.click(screen.getByRole('button', { name: /Nahrát vše/i }));
    });

    // Wait until uploading state is active
    await waitFor(() => {
      expect(screen.getByText('Nahrávám…')).toBeInTheDocument();
    });

    // Drop a new file while the first is uploading
    act(() => {
      simulateDrop(dropZone, [makeFile('newfile.txt', 'text/plain')]);
    });

    // The new file should appear in the list with "Čeká" status
    expect(screen.getByText('newfile.txt')).toBeInTheDocument();
    const waitingStatuses = screen.getAllByText('Čeká');
    expect(waitingStatuses.length).toBeGreaterThanOrEqual(1);

    // mutateAsync should only have been called once (for the original file)
    expect(mockMutateAsync).toHaveBeenCalledTimes(1);

    // Verify the new file was not passed to mutateAsync - only the original file was uploaded
    expect(mockMutateAsync.mock.calls[0][0].name).toBe('uploading.pdf');
  });
});
