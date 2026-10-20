import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import Pager from '../Pager';

describe('Pager', () => {
  it('renders nothing when everything fits on one page', () => {
    render(<Pager page={1} pageSize={25} totalCount={10} onPageChange={jest.fn()} />);

    expect(screen.queryByRole('button', { name: 'Další' })).not.toBeInTheDocument();
  });

  it('moves between pages and disables the ends', () => {
    const onPageChange = jest.fn();
    render(<Pager page={1} pageSize={25} totalCount={60} onPageChange={onPageChange} />);

    expect(screen.getByText('Strana 1 z 3 · celkem 60')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Předchozí' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Další' }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});
