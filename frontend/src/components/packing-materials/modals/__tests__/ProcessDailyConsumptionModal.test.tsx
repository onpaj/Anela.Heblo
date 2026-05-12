import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import ProcessDailyConsumptionModal from '../ProcessDailyConsumptionModal';

jest.mock('../../../../api/hooks/usePackingMaterials', () => ({
  useProcessDailyConsumption: jest.fn(),
}));

jest.mock('../../../../api/generated/api-client', () => ({
  ProcessDailyConsumptionRequest: jest.fn().mockImplementation((data: unknown) => data),
}));

const { useProcessDailyConsumption } = require('../../../../api/hooks/usePackingMaterials');

function setupMutationMock(resolveValue: {
  success: boolean;
  materialsProcessed?: number;
  message?: string;
}) {
  useProcessDailyConsumption.mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(resolveValue),
    isPending: false,
    error: null,
  });
}

const defaultProps = {
  isOpen: true,
  onClose: jest.fn(),
  onSuccess: jest.fn(),
};

describe('ProcessDailyConsumptionModal', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows green success banner when materials were updated', async () => {
    setupMutationMock({ success: true, materialsProcessed: 3 });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/odečteno pro 3 materiálů/i)).toBeInTheDocument();
    });

    expect(screen.queryByText(/nebyly nalezeny žádné faktury/i)).not.toBeInTheDocument();
  });

  it('shows yellow info banner when no invoices were found (zero materials processed)', async () => {
    setupMutationMock({ success: true, materialsProcessed: 0 });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/nebyly nalezeny žádné faktury/i)).toBeInTheDocument();
    });

    expect(screen.queryByText(/odečteno pro/i)).not.toBeInTheDocument();
  });

  it('shows red error banner when mutation throws', async () => {
    useProcessDailyConsumption.mockReturnValue({
      mutateAsync: jest.fn().mockRejectedValue(new Error('Server error')),
      isPending: false,
      error: new Error('Server error'),
    });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/server error/i)).toBeInTheDocument();
    });
  });

  it('shows red error banner when server reports already processed', async () => {
    useProcessDailyConsumption.mockReturnValue({
      mutateAsync: jest.fn().mockResolvedValue({
        success: false,
        materialsProcessed: 0,
        message: 'Daily consumption for 2025-06-15 was already processed',
      }),
      isPending: false,
      error: null,
    });

    render(<ProcessDailyConsumptionModal {...defaultProps} />);
    fireEvent.submit(screen.getByRole('button', { name: /odečíst spotřebu/i }));

    await waitFor(() => {
      expect(screen.getByText(/already processed/i)).toBeInTheDocument();
    });
  });
});
