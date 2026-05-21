import { render, screen } from '@testing-library/react';
import PackingItems, { PHOTO_ITEM_LIMIT } from '../PackingItems';
import type { PackingOrderItem } from '../../../api/hooks/useScanPackingOrder';

const makeItems = (count: number): PackingOrderItem[] =>
  Array.from({ length: count }, (_, i) => ({
    name: `Produkt ${i + 1}`,
    quantity: i + 1,
    imageUrl: null,
    setName: null,
  }));

describe('PackingItems', () => {
  it('renders a photo grid when item count is at or below the limit', () => {
    render(<PackingItems items={makeItems(PHOTO_ITEM_LIMIT)} />);
    expect(screen.getByTestId('packing-items-grid')).toBeInTheDocument();
  });

  it('renders a dense list when item count exceeds the limit', () => {
    render(<PackingItems items={makeItems(PHOTO_ITEM_LIMIT + 1)} />);
    expect(screen.getByTestId('packing-items-list')).toBeInTheDocument();
  });

  it('shows every item name and quantity', () => {
    render(<PackingItems items={makeItems(3)} />);
    expect(screen.getByText('Produkt 1')).toBeInTheDocument();
    expect(screen.getByText('Produkt 3')).toBeInTheDocument();
    expect(screen.getByText('3×')).toBeInTheDocument();
  });
});
