import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { DetailActionButtons } from '../DetailActionButtons';
import { ManufactureOrderState } from '../../../../api/generated/api-client';

const baseProps = {
  onCancel: jest.fn(),
  onDuplicate: jest.fn(),
  onClose: jest.fn(),
  onSave: jest.fn(),
  isUpdateLoading: false,
  isDuplicateLoading: false,
};

describe('DetailActionButtons', () => {
  afterEach(() => jest.clearAllMocks());

  test('does NOT render Tisk protokolu button when order is not Completed', () => {
    const order = { state: ManufactureOrderState.Planned };
    const onPrintProtocol = jest.fn();

    render(
      <DetailActionButtons
        {...baseProps}
        order={order}
        onPrintProtocol={onPrintProtocol}
      />
    );

    expect(screen.queryByText('Tisk protokolu')).not.toBeInTheDocument();
  });

  test('does NOT render Tisk protokolu button when order is Draft', () => {
    const order = { state: ManufactureOrderState.Draft };
    const onPrintProtocol = jest.fn();

    render(
      <DetailActionButtons
        {...baseProps}
        order={order}
        onPrintProtocol={onPrintProtocol}
      />
    );

    expect(screen.queryByText('Tisk protokolu')).not.toBeInTheDocument();
  });

  test('renders Tisk protokolu button when order state is Completed', () => {
    const order = { state: ManufactureOrderState.Completed };
    const onPrintProtocol = jest.fn();

    render(
      <DetailActionButtons
        {...baseProps}
        order={order}
        onPrintProtocol={onPrintProtocol}
      />
    );

    expect(screen.getByText('Tisk protokolu')).toBeInTheDocument();
  });

  test('calls onPrintProtocol when Tisk protokolu button is clicked', () => {
    const order = { state: ManufactureOrderState.Completed };
    const onPrintProtocol = jest.fn();

    render(
      <DetailActionButtons
        {...baseProps}
        order={order}
        onPrintProtocol={onPrintProtocol}
      />
    );

    fireEvent.click(screen.getByText('Tisk protokolu'));
    expect(onPrintProtocol).toHaveBeenCalledTimes(1);
  });

  test('does NOT render Tisk protokolu button when onPrintProtocol is not provided', () => {
    const order = { state: ManufactureOrderState.Completed };

    render(
      <DetailActionButtons
        {...baseProps}
        order={order}
      />
    );

    expect(screen.queryByText('Tisk protokolu')).not.toBeInTheDocument();
  });

  test('Tisk protokolu button is disabled when isPrintingProtocol is true', () => {
    const order = { state: ManufactureOrderState.Completed };
    const onPrintProtocol = jest.fn();

    render(
      <DetailActionButtons
        {...baseProps}
        order={order}
        onPrintProtocol={onPrintProtocol}
        isPrintingProtocol={true}
      />
    );

    const button = screen.getByTitle('Tisknout protokol výroby');
    expect(button).toBeDisabled();
  });
});
