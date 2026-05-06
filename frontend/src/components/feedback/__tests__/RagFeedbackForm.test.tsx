import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import RagFeedbackForm from '../RagFeedbackForm';

describe('RagFeedbackForm', () => {
  const baseProps = {
    onSubmit: jest.fn(),
    isSubmitting: false,
    isSuccess: false,
    alreadySubmitted: false,
  };

  beforeEach(() => jest.clearAllMocks());

  it('disables submit until both scores are selected', () => {
    render(<RagFeedbackForm {...baseProps} />);
    const button = screen.getByRole('button', { name: /Odeslat/i });
    expect(button).toBeDisabled();

    fireEvent.click(screen.getAllByText('4')[0]);
    expect(button).toBeDisabled();

    fireEvent.click(screen.getAllByText('5')[1]);
    expect(button).not.toBeDisabled();
  });

  it('passes scores and trimmed comment to onSubmit', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...baseProps} onSubmit={onSubmit} />);

    fireEvent.click(screen.getAllByText('5')[0]);
    fireEvent.click(screen.getAllByText('4')[1]);
    fireEvent.change(screen.getByPlaceholderText(/Volitelný/i), {
      target: { value: '  great answer  ' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Odeslat/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      precisionScore: 5,
      styleScore: 4,
      comment: 'great answer',
    });
  });

  it('omits empty comment from submit payload', () => {
    const onSubmit = jest.fn();
    render(<RagFeedbackForm {...baseProps} onSubmit={onSubmit} />);

    fireEvent.click(screen.getAllByText('3')[0]);
    fireEvent.click(screen.getAllByText('3')[1]);
    fireEvent.click(screen.getByRole('button', { name: /Odeslat/i }));

    expect(onSubmit).toHaveBeenCalledWith({
      precisionScore: 3,
      styleScore: 3,
      comment: undefined,
    });
  });

  it('renders success message and hides form when isSuccess', () => {
    render(<RagFeedbackForm {...baseProps} isSuccess />);
    expect(screen.getByText(/Děkujeme/)).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('renders already-submitted message when alreadySubmitted', () => {
    render(<RagFeedbackForm {...baseProps} alreadySubmitted />);
    expect(screen.getByText(/již byla odeslána/)).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('disables submit while isSubmitting even when scores selected', () => {
    render(<RagFeedbackForm {...baseProps} isSubmitting />);
    fireEvent.click(screen.getAllByText('5')[0]);
    fireEvent.click(screen.getAllByText('5')[1]);
    expect(screen.getByRole('button', { name: /Odeslat/i })).toBeDisabled();
  });
});
