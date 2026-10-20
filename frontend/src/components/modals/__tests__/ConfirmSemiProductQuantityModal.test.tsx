import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import ConfirmSemiProductQuantityModal from '../ConfirmSemiProductQuantityModal';

const baseProps = {
  isOpen: true,
  onClose: jest.fn(),
  orderId: 1,
  plannedQuantity: 1000,
  productName: 'Ochráním tváře',
};

describe('ConfirmSemiProductQuantityModal', () => {
  afterEach(() => jest.clearAllMocks());

  test('shows the missing materials when manufacture is refused for insufficient stock', async () => {
    const stockShortage = {
      response: JSON.stringify({
        success: false,
        errorCode: 'ManufactureInsufficientMaterialStock',
        params: { detail: 'Nedostatečné zásoby pro výrobu. Chybějící ingredience: Glycerol (AKL007): Required 10.00, Available 2.00' },
      }),
    };
    const onSubmit = jest.fn().mockRejectedValue(stockShortage);
    render(<ConfirmSemiProductQuantityModal {...baseProps} onSubmit={onSubmit} />);

    fireEvent.click(screen.getByRole('button', { name: 'Potvrdit výrobu' }));

    expect(await screen.findByText(/AKL007\): Required 10\.00, Available 2\.00/)).toBeInTheDocument();
  });

  test('keeps the generic message for other failures', async () => {
    const onSubmit = jest.fn().mockRejectedValue(new Error('network down'));
    render(<ConfirmSemiProductQuantityModal {...baseProps} onSubmit={onSubmit} />);

    fireEvent.click(screen.getByRole('button', { name: 'Potvrdit výrobu' }));

    expect(await screen.findByText('Chyba při potvrzení množství. Zkuste to prosím znovu.')).toBeInTheDocument();
  });
});
