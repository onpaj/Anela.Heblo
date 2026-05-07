import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import TagsTab from '../TagsTab';

// ---- Mocks ------------------------------------------------------------------

jest.mock('../../../../../api/hooks/usePhotobank', () => ({
  usePhotoTags: jest.fn(),
  useCreateTag: jest.fn(),
  useDeleteTag: jest.fn(),
}));

const { usePhotoTags, useCreateTag, useDeleteTag } = jest.requireMock(
  '../../../../../api/hooks/usePhotobank',
) as {
  usePhotoTags: jest.Mock;
  useCreateTag: jest.Mock;
  useDeleteTag: jest.Mock;
};

// ---- Helpers ----------------------------------------------------------------

const TAGS = [
  { id: 1, name: 'produkty', count: 10 },
  { id: 2, name: 'akce', count: 0 },
  { id: 3, name: 'promo', count: 3 },
];

function buildMockMutation(
  overrides: Partial<{ mutate: jest.Mock; mutateAsync: jest.Mock; isPending: boolean }> = {},
) {
  return {
    mutate: jest.fn(),
    mutateAsync: jest.fn().mockResolvedValue({}),
    isPending: false,
    ...overrides,
  };
}

function buildDefaultMocks() {
  usePhotoTags.mockReturnValue({ data: TAGS, isLoading: false });
  useCreateTag.mockReturnValue(buildMockMutation());
  useDeleteTag.mockReturnValue(buildMockMutation());
}

// ---- Tests ------------------------------------------------------------------

beforeEach(() => {
  buildDefaultMocks();
});

test('renders the tag list from mocked usePhotoTags', () => {
  render(<TagsTab />);

  expect(screen.getByText('produkty')).toBeInTheDocument();
  expect(screen.getByText('akce')).toBeInTheDocument();
  expect(screen.getByText('promo')).toBeInTheDocument();
  expect(screen.getByText('10 fotek')).toBeInTheDocument();
  expect(screen.getByText('0 fotek')).toBeInTheDocument();
  expect(screen.getByText('3 fotek')).toBeInTheDocument();
});

test('submit button is disabled when input is empty', () => {
  render(<TagsTab />);

  const submitBtn = screen.getByRole('button', { name: /Přidat štítek/i });
  expect(submitBtn).toBeDisabled();
});

test('calls useCreateTag mutateAsync when form is submitted', async () => {
  const mutateAsync = jest.fn().mockResolvedValue({});
  useCreateTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  render(<TagsTab />);

  fireEvent.change(screen.getByPlaceholderText('Název štítku'), {
    target: { value: 'nový štítek' },
  });
  fireEvent.click(screen.getByRole('button', { name: /Přidat štítek/i }));

  await waitFor(() => {
    expect(mutateAsync).toHaveBeenCalledWith('nový štítek');
  });

  // Input cleared on success
  await waitFor(() => {
    expect(screen.getByPlaceholderText('Název štítku')).toHaveValue('');
  });
});

test('calls useDeleteTag mutate immediately when trash clicked on tag with count=0', () => {
  const mutate = jest.fn();
  useDeleteTag.mockReturnValue(buildMockMutation({ mutate }));

  render(<TagsTab />);

  // 'akce' has count=0
  fireEvent.click(screen.getByRole('button', { name: 'Smazat štítek akce' }));

  expect(mutate).toHaveBeenCalledWith(2, expect.objectContaining({ onSettled: expect.any(Function) }));
});

test('opens confirm dialog when trash clicked on tag with count > 0', () => {
  render(<TagsTab />);

  // 'produkty' has count=10 — should open confirm dialog
  fireEvent.click(screen.getByRole('button', { name: 'Smazat štítek produkty' }));

  expect(screen.getByText(/Smazat štítek\?/i)).toBeInTheDocument();
  // Dialog body contains tag name in the confirmation message
  expect(screen.getByText(/přiřazen k 10 fotografiím/)).toBeInTheDocument();
});

test('calls useDeleteTag mutate when confirm dialog is confirmed', () => {
  const mutate = jest.fn();
  useDeleteTag.mockReturnValue(buildMockMutation({ mutate }));

  render(<TagsTab />);

  // Open dialog for 'produkty' (count=10)
  fireEvent.click(screen.getByRole('button', { name: 'Smazat štítek produkty' }));
  // Click the dialog's confirm button (exact text "Smazat", distinct from trash aria-labels)
  fireEvent.click(screen.getByRole('button', { name: 'Smazat' }));

  expect(mutate).toHaveBeenCalledWith(1, expect.objectContaining({ onSettled: expect.any(Function) }));
});

test('shows "Štítek již existuje" when response has alreadyExisted=true', async () => {
  const mutateAsync = jest.fn().mockResolvedValue({ alreadyExisted: true });
  useCreateTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  render(<TagsTab />);

  fireEvent.change(screen.getByPlaceholderText('Název štítku'), {
    target: { value: 'produkty' },
  });
  fireEvent.click(screen.getByRole('button', { name: /Přidat štítek/i }));

  await waitFor(() => {
    expect(screen.getByText('Štítek již existuje')).toBeInTheDocument();
  });
});

test('does NOT call useDeleteTag mutate when confirm dialog is cancelled', () => {
  const mutate = jest.fn();
  useDeleteTag.mockReturnValue(buildMockMutation({ mutate }));

  render(<TagsTab />);

  // Open dialog for 'promo' (count=3)
  fireEvent.click(screen.getByRole('button', { name: 'Smazat štítek promo' }));
  // Click cancel
  fireEvent.click(screen.getByRole('button', { name: /Zrušit/i }));

  expect(mutate).not.toHaveBeenCalled();
});
