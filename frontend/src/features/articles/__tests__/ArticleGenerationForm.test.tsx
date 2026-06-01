import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ArticleGenerationForm from '../ArticleGenerationForm';

// Define mocks as module-level variables prefixed with 'mock'
let mockMutate = jest.fn();

jest.mock('../../../api/hooks/useArticles', () => ({
  useGenerateArticleMutation: () => ({
    mutate: mockMutate,
    isPending: false,
    error: null,
  }),
}));

jest.mock('../../../api/hooks/useMarketingWriterPermission', () => ({
  useMarketingWriterPermission: () => true,
}));

describe('ArticleGenerationForm', () => {
  beforeEach(() => {
    mockMutate.mockReset();
  });

  it('renders the language note input with a 500-character limit', () => {
    render(<ArticleGenerationForm onArticleCreated={() => {}} />);
    const input = screen.getByPlaceholderText(/krátké věty, vyhýbat se odborným termínům/) as HTMLInputElement;
    expect(input).toBeInTheDocument();
    expect(input.maxLength).toBe(500);
    expect(input.required).toBe(false);
  });

  it('passes the trimmed languageNote on submit', () => {
    render(<ArticleGenerationForm onArticleCreated={() => {}} />);

    fireEvent.change(screen.getByPlaceholderText(/Výhody fermentovaných surovin/), {
      target: { value: 'Sun care basics' },
    });
    fireEvent.change(screen.getByPlaceholderText(/krátké věty, vyhýbat se odborným termínům/), {
      target: { value: '  krátké věty  ' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Generovat článek/ }));

    expect(mockMutate).toHaveBeenCalledTimes(1);
    const request = mockMutate.mock.calls[0][0];
    expect(request.languageNote).toBe('krátké věty');
  });

  it('submits languageNote as undefined when empty', () => {
    render(<ArticleGenerationForm onArticleCreated={() => {}} />);

    fireEvent.change(screen.getByPlaceholderText(/Výhody fermentovaných surovin/), {
      target: { value: 'Sun care basics' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Generovat článek/ }));

    expect(mockMutate).toHaveBeenCalledTimes(1);
    const request = mockMutate.mock.calls[0][0];
    expect(request.languageNote).toBeUndefined();
  });
});
