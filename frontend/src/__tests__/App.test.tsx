import React, { act } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import TestApp from '../TestApp';

test('renders app without crashing', async () => {
  await act(async () => {
    render(<TestApp />);
  });
  
  // App should render without throwing errors - if it doesn't crash, test passes
  expect(true).toBe(true);
});

test('app renders with proper structure', async () => {
  await act(async () => {
    render(<TestApp />);
  });
  
  // Test that the app div renders successfully
  const appElement = screen.getByTestId('app');
  expect(appElement).toBeInTheDocument();
});