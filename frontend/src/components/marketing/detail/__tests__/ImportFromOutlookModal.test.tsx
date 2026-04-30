import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import ImportFromOutlookModal from '../ImportFromOutlookModal';

const mockMutateAsync = jest.fn();
const mockReset = jest.fn();

jest.mock('../../../../api/hooks/useMarketingCalendar', () => ({
  useImportFromOutlook: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
    isError: false,
    reset: mockReset,
  }),
}));

const defaultProps = {
  isOpen: true,
  onClose: jest.fn(),
};

function fillDates() {
  const fromInput = screen.getByLabelText('Od');
  const toInput = screen.getByLabelText('Do');
  fireEvent.change(fromInput, { target: { value: '2026-05-01' } });
  fireEvent.change(toInput, { target: { value: '2026-05-31' } });
}

async function triggerImport() {
  fillDates();
  fireEvent.click(screen.getByRole('button', { name: /importovat/i }));
  // Wait for the result summary to appear (means setResult has been called)
  await waitFor(() => expect(screen.getByText('Vytvořeno:')).toBeInTheDocument());
}

beforeEach(() => {
  jest.clearAllMocks();
});

describe('ImportFromOutlookModal — UnmappedCategoriesPanel integration', () => {
  it('does not render unmapped panel when response has empty list', async () => {
    mockMutateAsync.mockResolvedValue({
      created: 5,
      skipped: 0,
      failed: 0,
      unmappedCategories: [],
    });

    render(<ImportFromOutlookModal {...defaultProps} />);
    await triggerImport();

    expect(screen.queryByText(/Nemapované kategorie z Outlooku/)).toBeNull();
  });

  it('does not render unmapped panel before any import has run', () => {
    render(<ImportFromOutlookModal {...defaultProps} />);

    expect(screen.queryByText(/Nemapované kategorie z Outlooku/)).toBeNull();
  });

  it('renders unmapped panel with names when response has unmapped categories', async () => {
    mockMutateAsync.mockResolvedValue({
      created: 5,
      skipped: 0,
      failed: 0,
      unmappedCategories: ['PR – jaro', 'Wellness kampaň'],
    });

    render(<ImportFromOutlookModal {...defaultProps} />);
    await triggerImport();

    expect(screen.getByText(/Nemapované kategorie z Outlooku/)).toBeInTheDocument();
    expect(screen.getByText('PR – jaro')).toBeInTheDocument();
    expect(screen.getByText('Wellness kampaň')).toBeInTheDocument();
  });

  it('panel appears after the summary row in document order', async () => {
    mockMutateAsync.mockResolvedValue({
      created: 5,
      skipped: 0,
      failed: 0,
      unmappedCategories: ['PR – jaro', 'Wellness kampaň'],
    });

    render(<ImportFromOutlookModal {...defaultProps} />);
    await triggerImport();

    const summaryText = screen.getByText('Vytvořeno:');
    const panelEl = screen.getByRole('status', { name: /Nemapované kategorie z Outlooku/ });

    // DOCUMENT_POSITION_FOLLOWING = 4 — panelEl comes after summaryText in the DOM
    const position = summaryText.compareDocumentPosition(panelEl);
    expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('is defensive against undefined unmappedCategories (no panel, no crash)', async () => {
    mockMutateAsync.mockResolvedValue({
      created: 3,
      skipped: 1,
      failed: 0,
    } as any);

    render(<ImportFromOutlookModal {...defaultProps} />);
    await triggerImport();

    expect(screen.queryByText(/Nemapované kategorie z Outlooku/)).toBeNull();
    // Summary still renders
    expect(screen.getByText('Vytvořeno:')).toBeInTheDocument();
  });
});
