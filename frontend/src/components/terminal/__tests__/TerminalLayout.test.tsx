import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import TerminalLayout from '../TerminalLayout';

jest.mock('../../auth/UserProfile', () => ({
  __esModule: true,
  default: () => <div data-testid="user-profile" />,
}));

const renderWithRouter = (initialPath: string) =>
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/terminal/*" element={<TerminalLayout />}>
          <Route index element={<div>Home content</div>} />
          <Route path="receive" element={<div>Receive content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );

describe('TerminalLayout', () => {
  it('renders the app title', () => {
    renderWithRouter('/terminal');
    expect(screen.getByText('Heblo Terminál')).toBeInTheDocument();
  });

  it('hides back button on /terminal (home)', () => {
    renderWithRouter('/terminal');
    expect(screen.queryByRole('button', { name: /zpět/i })).not.toBeInTheDocument();
  });

  it('shows back button on sub-routes', () => {
    renderWithRouter('/terminal/receive');
    expect(screen.getByRole('button', { name: /zpět/i })).toBeInTheDocument();
  });

  it('renders child route content via Outlet', () => {
    renderWithRouter('/terminal/receive');
    expect(screen.getByText('Receive content')).toBeInTheDocument();
  });

  it('renders user profile', () => {
    renderWithRouter('/terminal');
    expect(screen.getByTestId('user-profile')).toBeInTheDocument();
  });
});
