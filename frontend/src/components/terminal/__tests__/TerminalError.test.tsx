import React from 'react';
import { render, screen } from '@testing-library/react';
import TerminalError from '../TerminalError';

describe('TerminalError', () => {
  it('renders data-testid="terminal-error" on the outermost element', () => {
    render(<TerminalError message="Test error" />);
    expect(screen.getByTestId('terminal-error')).toBeInTheDocument();
  });

  it('renders the message prop text', () => {
    render(<TerminalError message="This is an error" />);
    expect(screen.getByText('This is an error')).toBeInTheDocument();
  });

  it('renders hint when provided', () => {
    render(
      <TerminalError message="Error message" hint="This is a hint" />
    );
    expect(screen.getByText('This is a hint')).toBeInTheDocument();
  });

  it('does not render hint element when hint is omitted', () => {
    render(<TerminalError message="Error message" />);
    // Hint text should not be in the document when prop is omitted
    expect(screen.queryByText('This is a hint')).not.toBeInTheDocument();
  });
});
