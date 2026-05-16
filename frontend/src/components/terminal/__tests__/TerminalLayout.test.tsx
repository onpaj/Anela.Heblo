import React from 'react';
import { render, screen } from '@testing-library/react';
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

// The manifest <link> lives in document.head, outside the render tree, so
// Testing Library queries cannot reach it — direct DOM access is required here.
const getManifestHref = () =>
  // eslint-disable-next-line testing-library/no-node-access
  document.head.querySelector('link[rel="manifest"]')?.getAttribute('href');

describe('TerminalLayout', () => {
  beforeEach(() => {
    // eslint-disable-next-line testing-library/no-node-access
    document.head.querySelectorAll('link[rel="manifest"]').forEach((link) => link.remove());
    const link = document.createElement('link');
    link.setAttribute('rel', 'manifest');
    link.setAttribute('href', '/manifest.json');
    document.head.appendChild(link);
  });

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

  it('links the terminal manifest while mounted', () => {
    renderWithRouter('/terminal');
    expect(getManifestHref()).toBe('/manifest.terminal.json');
  });

  it('restores the main manifest on unmount', () => {
    const { unmount } = renderWithRouter('/terminal');
    expect(getManifestHref()).toBe('/manifest.terminal.json');

    unmount();
    expect(getManifestHref()).toBe('/manifest.json');
  });
});
