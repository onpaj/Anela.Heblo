import React from 'react';
import { render, screen } from '@testing-library/react';
import TestApp from '../components/test/TestApp';

test('renders app without crashing', () => {
  render(<TestApp />);
  
  // App should render without throwing errors - if it doesn't crash, test passes
  expect(true).toBe(true);
});

test('app renders with proper structure', () => {
  render(<TestApp />);
  
  // Test that the app div renders successfully
  const appElement = screen.getByTestId('app');
  expect(appElement).toBeInTheDocument();
});