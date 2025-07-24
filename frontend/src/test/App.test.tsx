import React from 'react';
import { render, screen } from '@testing-library/react';
import App from '../App';

test('renders welcome message', () => {
  render(<App />);
  const welcomeElement = screen.getByText(/Vítejte v Anela Heblo/i);
  expect(welcomeElement).toBeInTheDocument();
});