import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import RagFeedbackForm from '../RagFeedbackForm';

const defaultProps = {
  onSubmit: jest.fn(),
  isSubmitting: false,
  alreadySubmitted: false,
  isSuccess: false,
};

describe('RagFeedbackForm', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('renders the form in idle state', () => {
    render(<RagFeedbackForm {...defaultProps} />);
    expect(screen.getByText('Ohodnoťte odpověď')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Volitelný komentář...')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' })).toBeInTheDocument();
  });

  it('shows success message when isSuccess is true', () => {
    render(<RagFeedbackForm {...defaultProps} isSuccess />);
    expect(screen.getByText('Děkujeme za vaši zpětnou vazbu.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Odeslat zpětnou vazbu' })).not.toBeInTheDocument();
  });

  it('shows already-submitted message when alreadySubmitted is true', () => {
    render(<RagFeedbackForm {...defaultProps} alreadySubmitted />);
    expect(screen.getByText('Zpětná vazba již byla odeslána.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Odeslat zpětnou vazbu' })).not.toBeInTheDocument();
  });

  it('submit button is disabled when scores are not selected', () => {
    render(<RagFeedbackForm {...defaultProps} />);
    expect(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' })).toBeDisabled();
  });

  it('submit button is enabled when both scores are selected', () => {
    render(<RagFeedbackForm {...defaultProps} />);

    const radioButtons = screen.getAllByRole('radio');
    // First row (Přesnost): scores 1,2,3,4,5 at indices 0,1,2,3,4 → select score 4 at index 3
    fireEvent.click(radioButtons[3]);
    // Second row (Styl): scores 1,2,3,4,5 at indices 5,6,7,8,9 → select score 3 at index 7
    fireEvent.click(radioButtons[7]);

    expect(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' })).not.toBeDisabled();
  });

  it('calls onSubmit with correct payload when submitted', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...defaultProps} onSubmit={onSubmit} />);

    const radioButtons = screen.getAllByRole('radio');
    // Select precision score 4 (index 3: first row, position 3)
    fireEvent.click(radioButtons[3]);
    // Select style score 3 (index 7: second row, position 2)
    fireEvent.click(radioButtons[7]);

    // Add comment
    fireEvent.change(screen.getByPlaceholderText('Volitelný komentář...'), {
      target: { value: 'Good answer' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' }));

    expect(onSubmit).toHaveBeenCalledWith({
      precisionScore: 4,
      styleScore: 3,
      comment: 'Good answer',
    });
  });

  it('calls onSubmit without comment when comment is empty', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...defaultProps} onSubmit={onSubmit} />);

    const radioButtons = screen.getAllByRole('radio');
    // Select precision score 5 (index 4: first row, position 4)
    fireEvent.click(radioButtons[4]);
    // Select style score 5 (index 9: second row, position 4)
    fireEvent.click(radioButtons[9]);

    fireEvent.click(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' }));

    expect(onSubmit).toHaveBeenCalledWith({
      precisionScore: 5,
      styleScore: 5,
      comment: undefined,
    });
  });

  it('submit button is disabled when isSubmitting is true', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...defaultProps} isSubmitting onSubmit={onSubmit} />);

    const radioButtons = screen.getAllByRole('radio');
    // Even if scores are selected, isSubmitting disables the button
    fireEvent.click(radioButtons[0]);
    fireEvent.click(radioButtons[5]);

    expect(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' })).toBeDisabled();
  });

  it('trims whitespace from comment before submission', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...defaultProps} onSubmit={onSubmit} />);

    const radioButtons = screen.getAllByRole('radio');
    fireEvent.click(radioButtons[0]);
    fireEvent.click(radioButtons[5]);

    // Add comment with leading/trailing whitespace
    fireEvent.change(screen.getByPlaceholderText('Volitelný komentář...'), {
      target: { value: '  Some comment  ' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' }));

    expect(onSubmit).toHaveBeenCalledWith({
      precisionScore: 1,
      styleScore: 1,
      comment: 'Some comment',
    });
  });

  it('treats empty comment string as undefined', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...defaultProps} onSubmit={onSubmit} />);

    const radioButtons = screen.getAllByRole('radio');
    fireEvent.click(radioButtons[1]);
    fireEvent.click(radioButtons[6]);

    // Add only whitespace
    fireEvent.change(screen.getByPlaceholderText('Volitelný komentář...'), {
      target: { value: '   ' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Odeslat zpětnou vazbu' }));

    expect(onSubmit).toHaveBeenCalledWith({
      precisionScore: 2,
      styleScore: 2,
      comment: undefined,
    });
  });
});
