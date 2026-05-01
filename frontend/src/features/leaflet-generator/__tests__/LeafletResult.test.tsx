import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { LeafletResult } from '../LeafletResult';

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

    act(() => jest.advanceTimersByTime(2000));

    expect(screen.getByRole('button', { name: 'Kopírovat' })).toBeInTheDocument();
  });

  it('clicking regenerate fires onRegenerate callback', () => {
    const onRegenerate = jest.fn();

    render(<LeafletResult content="Some content" onRegenerate={onRegenerate} />);

    fireEvent.click(screen.getByRole('button', { name: 'Generovat znovu' }));

    expect(onRegenerate).toHaveBeenCalledTimes(1);
  });
});
