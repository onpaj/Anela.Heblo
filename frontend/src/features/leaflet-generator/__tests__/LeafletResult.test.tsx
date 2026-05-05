import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import LeafletResult from '../LeafletResult';

jest.mock('react-markdown', () => ({
  __esModule: true,
  default: ({ children }: { children: string }) => {
    const lines = children.split('\n');
    return (
      <div>
        {lines.map((line, i) => {
          const h1Match = line.match(/^#\s+(.+)/);
          if (h1Match) return <h1 key={i}>{h1Match[1]}</h1>;
          const h2Match = line.match(/^##\s+(.+)/);
          if (h2Match) return <h2 key={i}>{h2Match[1]}</h2>;
          return <p key={i}>{line}</p>;
        })}
      </div>
    );
  },
}));

jest.mock('../../../components/feedback/RagFeedbackForm', () => ({
  __esModule: true,
  default: ({ onSubmit, alreadySubmitted, isSuccess }: any) => (
    <div
      data-testid="rag-feedback-form"
      data-already-submitted={alreadySubmitted}
      data-is-success={isSuccess}
    >
      <button
        onClick={() => onSubmit({ precisionScore: 4, styleScore: 3 })}
        data-testid="feedback-submit-button"
      >
        Submit Feedback
      </button>
    </div>
  ),
}));

jest.mock('../../../api/hooks/useLeaflet', () => ({
  useSubmitLeafletFeedbackMutation: () => ({
    mutate: jest.fn(),
    isPending: false,
  }),
}));

Object.defineProperty(navigator, 'clipboard', {
  value: { writeText: jest.fn().mockResolvedValue(undefined) },
  writable: true,
});

describe('LeafletResult', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.clearAllMocks();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('renders nothing when content is empty', () => {
    render(<LeafletResult content="" onRegenerate={jest.fn()} />);

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('renders Markdown content as HTML', () => {
    render(<LeafletResult content="# Heading" onRegenerate={jest.fn()} />);

    const heading = screen.getByRole('heading', { level: 1 });
    expect(heading).toHaveTextContent('Heading');
  });

  it('copy button toggles label for 2 seconds then reverts', async () => {
    render(
      <LeafletResult content="Some content" onRegenerate={jest.fn()} />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Kopírovat' }));

    expect(
      await screen.findByRole('button', { name: 'Zkopírováno' })
    ).toBeInTheDocument();

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('Some content');

    act(() => jest.advanceTimersByTime(2000));

    expect(screen.getByRole('button', { name: 'Kopírovat' })).toBeInTheDocument();
  });

  it('does not toggle label when clipboard write fails', async () => {
    (navigator.clipboard.writeText as jest.Mock).mockRejectedValueOnce(
      new Error('Clipboard unavailable')
    );

    render(
      <LeafletResult content="Some content" onRegenerate={jest.fn()} />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Kopírovat' }));

    // Allow the rejected promise to settle
    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByRole('button', { name: 'Kopírovat' })).toBeInTheDocument();
  });

  it('clicking regenerate fires onRegenerate callback', () => {
    const onRegenerate = jest.fn();

    render(<LeafletResult content="Some content" onRegenerate={onRegenerate} />);

    fireEvent.click(screen.getByRole('button', { name: 'Generovat znovu' }));

    expect(onRegenerate).toHaveBeenCalledTimes(1);
  });

  it('does not render feedback form when generationId is absent', () => {
    render(<LeafletResult content="Some content" onRegenerate={jest.fn()} />);
    expect(screen.queryByTestId('rag-feedback-form')).not.toBeInTheDocument();
  });

  it('renders feedback form when generationId is provided', () => {
    render(
      <LeafletResult
        content="Some content"
        onRegenerate={jest.fn()}
        generationId="gen-123"
      />
    );
    expect(screen.getByTestId('rag-feedback-form')).toBeInTheDocument();
  });

  it('resets feedback state when generationId changes', () => {
    const { rerender } = render(
      <LeafletResult
        content="Some content"
        onRegenerate={jest.fn()}
        generationId="gen-123"
      />
    );

    expect(screen.getByTestId('rag-feedback-form')).toBeInTheDocument();
    const form1 = screen.getByTestId('rag-feedback-form');
    expect(form1.getAttribute('data-already-submitted')).toBe('false');
    expect(form1.getAttribute('data-is-success')).toBe('false');

    // Change generationId
    rerender(
      <LeafletResult
        content="Some content"
        onRegenerate={jest.fn()}
        generationId="gen-456"
      />
    );

    // Form should still be present but with reset state
    const form2 = screen.getByTestId('rag-feedback-form');
    expect(form2.getAttribute('data-already-submitted')).toBe('false');
    expect(form2.getAttribute('data-is-success')).toBe('false');
  });
});
