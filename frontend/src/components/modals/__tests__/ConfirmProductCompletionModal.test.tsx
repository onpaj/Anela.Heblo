import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
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
  test('shows the missing materials when completion is refused for insufficient stock', async () => {
    const stockShortage = {
      response: JSON.stringify({
        success: false,
        errorCode: 'ManufactureInsufficientMaterialStock',
        params: { detail: 'Nedostatečné zásoby pro výrobu. Chybějící ingredience: Etiketa - Ochráním tváře, 15 ml (ETI098): Required 728.00, Available 700.00' },
      }),
    };
    const onSubmit = jest.fn().mockRejectedValue(stockShortage);
    render(<ConfirmProductCompletionModal {...baseProps} onSubmit={onSubmit} products={[product]} />);

    fireEvent.click(screen.getByRole('button', { name: 'Potvrdit výrobu' }));

    expect(await screen.findByText(/ETI098\): Required 728\.00, Available 700\.00/)).toBeInTheDocument();
    expect(screen.getByText(/nebyla dokončena/)).toBeInTheDocument();
  });

  test('shows the missing materials when the distribution override is refused for insufficient stock', async () => {
    const stockShortage = {
      response: JSON.stringify({
        success: false,
        errorCode: 'ManufactureInsufficientMaterialStock',
        params: { detail: 'Nedostatečné zásoby pro výrobu. Chybějící ingredience: Glycerol (AKL007): Required 10.00, Available 2.00' },
      }),
    };
    const onConfirmDistribution = jest.fn().mockRejectedValue(stockShortage);
    const distributionPreview = { differencePercentage: 12, allowedResiduePercentage: 5, products: [] } as any;
    render(
      <ConfirmProductCompletionModal
        {...baseProps}
        products={[product]}
        distributionPreview={distributionPreview}
        onConfirmDistribution={onConfirmDistribution}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Potvrdit distribuci' }));

    expect(await screen.findByText(/AKL007\): Required 10\.00, Available 2\.00/)).toBeInTheDocument();
  });

  test('keeps the generic message for other failures', async () => {
    const onSubmit = jest.fn().mockRejectedValue(new Error('network down'));
    render(<ConfirmProductCompletionModal {...baseProps} onSubmit={onSubmit} products={[product]} />);

    fireEvent.click(screen.getByRole('button', { name: 'Potvrdit výrobu' }));

    expect(await screen.findByText('Chyba při potvrzení množství produktů. Zkuste to prosím znovu.')).toBeInTheDocument();
  });
});
