import React from 'react';
import { render, screen } from '@testing-library/react';
import { ProductsDataGrid } from '../ProductsDataGrid';
import { ManufactureType } from '../../../../api/generated/api-client';

const baseProps = {
  canEditFields: false,
  editableProductQuantities: {},
  onProductQuantityChange: jest.fn(),
};

describe('ProductsDataGrid', () => {
  afterEach(() => jest.clearAllMocks());

  test('SinglePhase: product matching the placeholder semiproduct renders as a normal product (no "Přímý výstup" badge, no g suffix)', () => {
    // For SinglePhase the semiproduct is a placeholder pointing at the first product,
    // so the product must NOT be treated as a direct bulk/grams output row.
    const order = {
      manufactureType: ManufactureType.SinglePhase,
      semiProduct: { productCode: 'P001' },
      products: [
        { productCode: 'P001', productName: 'Product 1', actualQuantity: 5 },
      ],
    };

    render(<ProductsDataGrid {...baseProps} order={order} />);

    expect(screen.queryByText('Přímý výstup')).not.toBeInTheDocument();
    // Quantity shown in pieces (no "g" suffix that direct-output rows use)
    expect(screen.getByText('5')).toBeInTheDocument();
  });

  test('MultiPhase: product matching the semiproduct still renders the "Přímý výstup" badge', () => {
    const order = {
      manufactureType: ManufactureType.MultiPhase,
      semiProduct: { productCode: 'SP001' },
      products: [
        { productCode: 'SP001', productName: 'Semi Product', actualQuantity: 200 },
      ],
    };

    render(<ProductsDataGrid {...baseProps} order={order} />);

    expect(screen.getByText('Přímý výstup')).toBeInTheDocument();
  });
});
