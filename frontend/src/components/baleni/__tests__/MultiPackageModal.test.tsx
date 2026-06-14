import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import MultiPackageModal from '../MultiPackageModal';

const noop = () => {};

describe('MultiPackageModal', () => {
  it('defaults to 2 packages', () => {
    render(<MultiPackageModal onConfirm={noop} onClose={noop} />);
    expect(screen.getByTestId('multi-package-count')).toHaveTextContent('2');
  });

  it('increments up to 10 then disables the plus button', () => {
    render(<MultiPackageModal onConfirm={noop} onClose={noop} />);
    const plus = screen.getByTestId('multi-package-increment');
    for (let i = 0; i < 20; i++) fireEvent.click(plus);
    expect(screen.getByTestId('multi-package-count')).toHaveTextContent('10');
    expect(plus).toBeDisabled();
  });

  it('does not decrement below 2', () => {
    render(<MultiPackageModal onConfirm={noop} onClose={noop} />);
    const minus = screen.getByTestId('multi-package-decrement');
    fireEvent.click(minus);
    expect(screen.getByTestId('multi-package-count')).toHaveTextContent('2');
    expect(minus).toBeDisabled();
  });

  it('confirms with the scanned order code and current count', () => {
    const onConfirm = jest.fn();
    render(<MultiPackageModal onConfirm={onConfirm} onClose={noop} />);
    fireEvent.click(screen.getByTestId('multi-package-increment')); // 3
    const input = screen.getByRole('textbox') as HTMLInputElement;
    fireEvent.change(input, { target: { value: '250001' } });
    fireEvent.submit(input.closest('form')!);
    expect(onConfirm).toHaveBeenCalledWith('250001', 3);
  });

  it('calls onClose when the close button is clicked', () => {
    const onClose = jest.fn();
    render(<MultiPackageModal onConfirm={noop} onClose={onClose} />);
    fireEvent.click(screen.getByTestId('multi-package-close'));
    expect(onClose).toHaveBeenCalled();
  });
});
