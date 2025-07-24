import React from 'react';
import { render, screen } from '@testing-library/react';
import App from '../App';

test('renders app without crashing', () => {
  render(<App />);
  // App should render without throwing errors
  // The actual content depends on authentication state and routing
  expect(document.body).toBeInTheDocument();
});

test('app contains main div', () => {
  render(<App />);
  const appDiv = document.querySelector('.App');
  expect(appDiv).toBeInTheDocument();
});