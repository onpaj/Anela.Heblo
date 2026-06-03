import React from 'react';
import { render, screen } from '@testing-library/react';
import ConfirmProductCompletionModal from '../ConfirmProductCompletionModal';

const baseProps = {
  isOpen: true,
  onClose: jest.fn(),
  onSubmit: jest.fn(),
  orderId: 1,
  isLoading: false,
  onConfirmDistribution: jest.fn(),
  onBackFromDistribution: jest.fn(),
};

const product = {
  id: 1,
  productCode: 'P001',
  productName: 'Product 1',
  plannedQuantity: 10,
};

describe('ConfirmProductCompletionModal', () => {
  afterEach(() => jest.clearAllMocks());

  test('SinglePhase: with no semiProductCode the product is not tagged as direct output (no "Přímý výstup", no "g" suffix)', () => {
    // The parent withholds semiProductCode for SinglePhase orders, so the product
    // matching the placeholder semiproduct must render as a normal product.
    render(
      <ConfirmProductCompletionModal {...baseProps} products={[product]} semiProductCode={undefined} />
    );

    expect(screen.queryByText('Přímý výstup')).not.toBeInTheDocument();
    // Planned quantity shown without the "g" suffix that direct-output rows use.
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.queryByText('10g')).not.toBeInTheDocument();
  });

  test('MultiPhase: product matching semiProductCode renders the "Přímý výstup" badge', () => {
    render(
      <ConfirmProductCompletionModal {...baseProps} products={[product]} semiProductCode="P001" />
    );

    expect(screen.getByText('Přímý výstup')).toBeInTheDocument();
  });
});
