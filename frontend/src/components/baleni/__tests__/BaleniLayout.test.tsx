import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import BaleniLayout from '../BaleniLayout';

jest.mock('../../auth/UserProfile', () => ({
  __esModule: true,
  default: () => <div data-testid="user-profile" />,
}));

jest.mock('../packingUser/PackingUserPicker', () => ({
  PackingUserPicker: () => null,
}));

const renderWithRouter = (initialPath: string) =>
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/baleni/*" element={<BaleniLayout />}>
          <Route index element={<div>Home content</div>} />
          <Route path="baleni" element={<div>Balení content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );

// The manifest <link> lives in document.head, outside the render tree, so
// Testing Library queries cannot reach it — direct DOM access is required here.
const getManifestHref = () =>
  // eslint-disable-next-line testing-library/no-node-access
  document.head.querySelector('link[rel="manifest"]')?.getAttribute('href');

describe('BaleniLayout', () => {
  beforeEach(() => {
    // eslint-disable-next-line testing-library/no-node-access
    document.head.querySelectorAll('link[rel="manifest"]').forEach((link) => link.remove());
    const link = document.createElement('link');
    link.setAttribute('rel', 'manifest');
    link.setAttribute('href', '/manifest.json');
    document.head.appendChild(link);
  });

  it('renders the app title', () => {
    renderWithRouter('/baleni');
    expect(screen.getByText('Heblo Balení')).toBeInTheDocument();
  });

  it('hides back button on /baleni (home)', () => {
    renderWithRouter('/baleni');
    expect(screen.queryByRole('button', { name: /zpět/i })).not.toBeInTheDocument();
  });

  it('shows back button on sub-routes', () => {
    renderWithRouter('/baleni/baleni');
    expect(screen.getByRole('button', { name: /zpět/i })).toBeInTheDocument();
  });

  it('renders child route content via Outlet', () => {
    renderWithRouter('/baleni/baleni');
    expect(screen.getByText('Balení content')).toBeInTheDocument();
  });

  it('renders user profile', () => {
    renderWithRouter('/baleni');
    expect(screen.getByTestId('user-profile')).toBeInTheDocument();
  });

  it('links the baleni manifest while mounted', () => {
    renderWithRouter('/baleni');
    expect(getManifestHref()).toBe('/manifest.baleni.json');
  });

  it('restores the main manifest on unmount', () => {
    const { unmount } = renderWithRouter('/baleni');
    expect(getManifestHref()).toBe('/manifest.baleni.json');

    unmount();
    expect(getManifestHref()).toBe('/manifest.json');
  });
});
