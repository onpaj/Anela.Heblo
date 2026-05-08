import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ConfirmDeleteTagDialog from '../ConfirmDeleteTagDialog';

const DEFAULT_PROPS = {
  isOpen: true,
  tagName: 'produkty',
  assignmentCount: 5,
  onConfirm: jest.fn(),
  onCancel: jest.fn(),
};

beforeEach(() => {
  (DEFAULT_PROPS.onConfirm as jest.Mock).mockClear();
  (DEFAULT_PROPS.onCancel as jest.Mock).mockClear();
});

test('renders dialog when isOpen is true with correct tag name and count', () => {
  render(<ConfirmDeleteTagDialog {...DEFAULT_PROPS} />);

  expect(screen.getByText(/Smazat štítek/i)).toBeInTheDocument();
  expect(screen.getByText(/produkty/)).toBeInTheDocument();
  expect(screen.getByText(/5 fotografiím/)).toBeInTheDocument();
});

test('does not render when isOpen is false', () => {
  render(<ConfirmDeleteTagDialog {...DEFAULT_PROPS} isOpen={false} />);

  expect(screen.queryByText(/Smazat štítek/i)).not.toBeInTheDocument();
});

test('calls onConfirm when confirm button is clicked', () => {
  render(<ConfirmDeleteTagDialog {...DEFAULT_PROPS} />);

  fireEvent.click(screen.getByRole('button', { name: /Smazat/i }));

  expect(DEFAULT_PROPS.onConfirm).toHaveBeenCalledTimes(1);
  expect(DEFAULT_PROPS.onCancel).not.toHaveBeenCalled();
});

test('calls onCancel when cancel button is clicked', () => {
  render(<ConfirmDeleteTagDialog {...DEFAULT_PROPS} />);

  fireEvent.click(screen.getByRole('button', { name: /Zrušit/i }));

  expect(DEFAULT_PROPS.onCancel).toHaveBeenCalledTimes(1);
  expect(DEFAULT_PROPS.onConfirm).not.toHaveBeenCalled();
});
