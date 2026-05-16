import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ExplainModal } from '../ExplainModal';

describe('ExplainModal', () => {
  const baseProps = {
    isOpen: true,
    onClose: jest.fn(),
    isLoading: false,
    relevantTranscript: null,
    explanation: null,
    error: null,
  };

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders nothing when isOpen is false', () => {
    render(<ExplainModal {...baseProps} isOpen={false} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows spinner while loading', () => {
    render(<ExplainModal {...baseProps} isLoading />);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows transcript and explanation on success', () => {
    render(<ExplainModal {...baseProps} relevantTranscript="slice of talk" explanation="reason here" />);
    expect(screen.getByText('slice of talk')).toBeInTheDocument();
    expect(screen.getByText('reason here')).toBeInTheDocument();
    expect(screen.getByText('Záznam konverzace')).toBeInTheDocument();
    expect(screen.getByText('Vysvětlení')).toBeInTheDocument();
  });

  it('shows error message when error is set', () => {
    render(<ExplainModal {...baseProps} error="Něco se pokazilo" />);
    expect(screen.getByText('Něco se pokazilo')).toBeInTheDocument();
  });

  it('calls onClose when close button clicked', () => {
    const onClose = jest.fn();
    render(<ExplainModal {...baseProps} onClose={onClose} />);
    fireEvent.click(screen.getByTitle('Zavřít'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape pressed', () => {
    const onClose = jest.fn();
    render(<ExplainModal {...baseProps} onClose={onClose} />);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('renders transcript as dialog lines when Speaker: format is detected', () => {
    const transcript = 'Andy: Tenhle kýbl musíme zkontrolovat. Peťa: No to je jasné, každý vypadá jinak.';
    render(<ExplainModal {...baseProps} relevantTranscript={transcript} explanation="reason" />);
    expect(screen.getByText('Andy')).toBeInTheDocument();
    expect(screen.getByText('Tenhle kýbl musíme zkontrolovat.')).toBeInTheDocument();
    expect(screen.getByText('Peťa')).toBeInTheDocument();
    expect(screen.getByText('No to je jasné, každý vypadá jinak.')).toBeInTheDocument();
  });

  it('falls back to plain text when transcript has no dialog format', () => {
    render(<ExplainModal {...baseProps} relevantTranscript="slice of talk" explanation="reason here" />);
    expect(screen.getByText('slice of talk')).toBeInTheDocument();
  });
});
