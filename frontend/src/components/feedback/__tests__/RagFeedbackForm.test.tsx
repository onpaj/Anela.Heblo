import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import RagFeedbackForm from '../RagFeedbackForm';

describe('RagFeedbackForm', () => {
  const noop = jest.fn();

  it('renders precision and style score buttons', () => {
    render(
      <RagFeedbackForm
        onSubmit={noop}
        isSubmitting={false}
        isError={false}
        feedbackState="idle"
      />
    );
    expect(screen.getByText('Přesnost')).toBeInTheDocument();
    expect(screen.getByText('Styl')).toBeInTheDocument();
  });

  it('submit button is disabled until both scores are selected', () => {
    render(
      <RagFeedbackForm
        onSubmit={noop}
        isSubmitting={false}
        isError={false}
        feedbackState="idle"
      />
    );
    const submit = screen.getByRole('button', { name: /odeslat/i });
    expect(submit).toBeDisabled();
  });

  it('calls onSubmit with scores and comment when submitted', () => {
    const handleSubmit = jest.fn();
    render(
      <RagFeedbackForm
        onSubmit={handleSubmit}
        isSubmitting={false}
        isError={false}
        feedbackState="idle"
      />
    );

    // Select precision score 4
    const precisionRadios = screen.getAllByRole('radio');
    fireEvent.click(precisionRadios[3]); // score 4 (index 3)

    // Select style score 5
    const styleRadios = screen.getAllByRole('radio');
    fireEvent.click(styleRadios[9]); // score 5 in style row (index 4 + 5 = 9)

    fireEvent.click(screen.getByRole('button', { name: /odeslat/i }));

    expect(handleSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ precisionScore: 4, styleScore: 5 })
    );
  });

  it('shows success message when feedbackState is submitted', () => {
    render(
      <RagFeedbackForm
        onSubmit={noop}
        isSubmitting={false}
        isError={false}
        feedbackState="submitted"
      />
    );
    expect(screen.getByText(/děkujeme/i)).toBeInTheDocument();
  });

  it('shows already-submitted message when feedbackState is alreadySubmitted', () => {
    render(
      <RagFeedbackForm
        onSubmit={noop}
        isSubmitting={false}
        isError={false}
        feedbackState="alreadySubmitted"
      />
    );
    expect(screen.getByText(/zpětná vazba již byla odeslána/i)).toBeInTheDocument();
  });

  it('shows error message when isError is true', () => {
    render(
      <RagFeedbackForm
        onSubmit={noop}
        isSubmitting={false}
        isError={true}
        feedbackState="idle"
      />
    );
    expect(screen.getByText(/odeslání selhalo/i)).toBeInTheDocument();
  });
});
