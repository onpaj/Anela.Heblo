import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ColumnChooser } from '../ColumnChooser';
import { GridColumn, GridColumnState } from '../types';

const mockColumns: GridColumn<{ id: string }>[] = [
  { id: 'name', header: 'Produkt', canHide: false, canReorder: false, renderCell: (r) => r.id },
  { id: 'stock', header: 'Skladem', renderCell: (r) => r.id },
  { id: 'reserve', header: 'Rezerva', renderCell: (r) => r.id },
];

const defaultState: GridColumnState[] = [
  { id: 'name', order: 0, hidden: false },
  { id: 'stock', order: 1, hidden: false },
  { id: 'reserve', order: 2, hidden: true },
];

function renderChooser(onToggle = jest.fn(), onReset = jest.fn()) {
  render(
    <ColumnChooser
      columns={mockColumns}
      columnState={defaultState}
      onToggle={onToggle}
      onReset={onReset}
    />,
  );
}

test('shows trigger button with label Sloupce', () => {
  renderChooser();
  expect(screen.getByRole('button', { name: /sloupce/i })).toBeInTheDocument();
});

test('opens popover and shows hidable columns on trigger click', () => {
  renderChooser();
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  expect(screen.getByText('Skladem')).toBeInTheDocument();
  expect(screen.getByText('Rezerva')).toBeInTheDocument();
});

test('does not show canHide:false columns in the list', () => {
  renderChooser();
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  // 'Produkt' has canHide: false — should not appear in chooser
  expect(screen.queryByLabelText('Produkt')).not.toBeInTheDocument();
});

test('calls onToggle when a column checkbox is clicked', () => {
  const onToggle = jest.fn();
  renderChooser(onToggle);
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  fireEvent.click(screen.getByLabelText('Skladem'));
  expect(onToggle).toHaveBeenCalledWith('stock');
});

test('hidden columns have unchecked checkbox', () => {
  renderChooser();
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  const reservaCheckbox = screen.getByLabelText('Rezerva') as HTMLInputElement;
  expect(reservaCheckbox.checked).toBe(false);
});

test('calls onReset and closes popover when reset button clicked', () => {
  const onReset = jest.fn();
  renderChooser(jest.fn(), onReset);
  fireEvent.click(screen.getByRole('button', { name: /sloupce/i }));
  fireEvent.click(screen.getByRole('button', { name: /reset/i }));
  expect(onReset).toHaveBeenCalledTimes(1);
  // Popover should close — 'Skladem' no longer visible
  expect(screen.queryByText('Skladem')).not.toBeInTheDocument();
});
