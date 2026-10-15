import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import ConfirmActionDialog from '../ConfirmActionDialog';

const renderDialog = (props: Partial<React.ComponentProps<typeof ConfirmActionDialog>> = {}) => {
  const onConfirm = jest.fn();
  const onCancel = jest.fn();
  render(
    <ConfirmActionDialog
      isOpen
      title="Vypnout provádění změn?"
      message="Heblo přestane provádět schválené návrhy."
      confirmLabel="Vypnout provádění"
      tone="primary"
      onConfirm={onConfirm}
      onCancel={onCancel}
      {...props}
    />,
  );
  return { onConfirm, onCancel };
};

describe('ConfirmActionDialog', () => {
  it('renders nothing when closed', () => {
    renderDialog({ isOpen: false });

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('calls onConfirm and onCancel from the two buttons', () => {
    const { onConfirm, onCancel } = renderDialog();

    fireEvent.click(screen.getByRole('button', { name: 'Vypnout provádění' }));
    fireEvent.click(screen.getByRole('button', { name: 'Zrušit' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('is labelled by its title', () => {
    renderDialog();

    expect(screen.getByRole('dialog', { name: 'Vypnout provádění změn?' })).toBeInTheDocument();
  });

  it('disables confirm while pending or when the caller marks it invalid', () => {
    renderDialog({ isConfirmDisabled: true });

    expect(screen.getByRole('button', { name: 'Vypnout provádění' })).toBeDisabled();
  });

  it('shows a progress label while pending', () => {
    renderDialog({ isPending: true });

    expect(screen.getByRole('button', { name: 'Ukládám…' })).toBeDisabled();
  });
});
