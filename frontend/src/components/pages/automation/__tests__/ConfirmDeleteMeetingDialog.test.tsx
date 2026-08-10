import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ConfirmDeleteMeetingDialog from '../ConfirmDeleteMeetingDialog';

const baseProps = {
  isOpen: true,
  subject: 'Schůzka s týmem',
  isDeleting: false,
  error: null,
  onConfirm: jest.fn(),
  onCancel: jest.fn(),
};

beforeEach(() => jest.clearAllMocks());

it('renders nothing when closed', () => {
  const { container } = render(<ConfirmDeleteMeetingDialog {...baseProps} isOpen={false} />);
  expect(container).toBeEmptyDOMElement();
});

it('names the meeting and explains what is deleted', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} />);
  expect(screen.getByText(/Schůzka s týmem/)).toBeInTheDocument();
  expect(screen.getByText(/přepis/i)).toBeInTheDocument();
  expect(screen.getByText(/souhrn/i)).toBeInTheDocument();
  expect(screen.getByText(/úkolů/i)).toBeInTheDocument();
  expect(screen.getByText(/oprávnění/i)).toBeInTheDocument();
  expect(screen.getByText(/Planneru/i)).toBeInTheDocument();
});

it('calls onConfirm when the delete button is clicked', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} />);
  fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));
  expect(baseProps.onConfirm).toHaveBeenCalledTimes(1);
});

it('calls onCancel when the cancel button is clicked', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} />);
  fireEvent.click(screen.getByRole('button', { name: /zrušit/i }));
  expect(baseProps.onCancel).toHaveBeenCalledTimes(1);
});

it('disables both buttons and shows progress while deleting', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} isDeleting />);
  expect(screen.getByRole('button', { name: /mažu/i })).toBeDisabled();
  expect(screen.getByRole('button', { name: /zrušit/i })).toBeDisabled();
});

it('shows the error message when deletion failed', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} error="Smazání se nezdařilo." />);
  expect(screen.getByText('Smazání se nezdařilo.')).toBeInTheDocument();
});
