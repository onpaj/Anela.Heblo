import React from 'react';
import { render, screen } from '@testing-library/react';
import App from '../App';

test('renders app without crashing', () => {
  render(<App />);
  // App should render without throwing errors - if it doesn't crash, test passes
  expect(true).toBe(true);
});

test('app renders with proper structure', () => {
  render(<App />);
  // Test that the app div renders successfully
  const appElement = screen.getByTestId('app');
  expect(appElement).toBeInTheDocument();
});