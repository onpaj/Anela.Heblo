import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { DockedAction } from '../DockedAction';

it('renders nothing when no actions', () => {
  render(<DockedAction actions={[]} />);
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

it('renders a single full-width action and fires onClick', () => {
  const onClick = jest.fn();
  render(<DockedAction actions={[{ label: 'Odeslat', onClick, testId: 'send' }]} />);
  fireEvent.click(screen.getByTestId('send'));
  expect(onClick).toHaveBeenCalled();
});

it('disables an action when disabled', () => {
  render(<DockedAction actions={[{ label: 'Odeslat', onClick: jest.fn(), disabled: true, testId: 'send' }]} />);
  expect(screen.getByTestId('send')).toBeDisabled();
});
