import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import KbTagFrequencyPanel from '../KbTagFrequencyPanel';

const stats = {
  problemTags: [{ tag: 'akné', label: 'akné', count: 5 }],
  productTags: [{ tag: 'hořčíkové máslo', label: 'hořčíkové máslo', count: 2 }],
  recommendedProducts: [{ tag: 'MAS009', label: 'Klidné nožky balzám', count: 3 }],
};

test('lists each kind with counts', () => {
  render(<KbTagFrequencyPanel stats={stats} isLoading={false} onProblemClick={jest.fn()} onProductClick={jest.fn()} />);
  expect(screen.getByRole('button', { name: /akné/ })).toHaveTextContent('5');
  expect(screen.getByText('Klidné nožky balzám')).toBeInTheDocument();
});

test('clicking a problem or asked-about product calls its handler', () => {
  const onProblemClick = jest.fn();
  const onProductClick = jest.fn();
  render(<KbTagFrequencyPanel stats={stats} isLoading={false} onProblemClick={onProblemClick} onProductClick={onProductClick} />);
  fireEvent.click(screen.getByRole('button', { name: /akné/ }));
  fireEvent.click(screen.getByRole('button', { name: /hořčíkové máslo/ }));
  expect(onProblemClick).toHaveBeenCalledWith('akné');
  expect(onProductClick).toHaveBeenCalledWith('hořčíkové máslo');
});

test('recommended products are not clickable filters', () => {
  render(<KbTagFrequencyPanel stats={stats} isLoading={false} onProblemClick={jest.fn()} onProductClick={jest.fn()} />);
  expect(screen.queryByRole('button', { name: /Klidné nožky balzám/ })).not.toBeInTheDocument();
});

test('shows an empty-state message when no tags exist yet', () => {
  render(
    <KbTagFrequencyPanel
      stats={{ problemTags: [], productTags: [], recommendedProducts: [] }}
      isLoading={false}
      onProblemClick={jest.fn()}
      onProductClick={jest.fn()}
    />,
  );
  expect(screen.getByText('Zatím žádné štítky v tomto období.')).toBeInTheDocument();
});
