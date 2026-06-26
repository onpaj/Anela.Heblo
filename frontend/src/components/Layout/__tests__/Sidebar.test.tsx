import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from '../Sidebar';
import { ACCESS_ROUTES } from '../../../auth/accessMatrix.generated';

let mockPermissions: string[] = [];
let mockIsSuperUser = false;

jest.mock('../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: mockPermissions,
    isSuperUser: mockIsSuperUser,
    groups: [],
    isLoading: false,
    hasPermission: (perm: string) =>
      mockIsSuperUser || mockPermissions.includes(perm),
  }),
}));
jest.mock('../../../contexts/ChangelogContext', () => ({ useChangelogContext: () => ({ openModal: jest.fn() }) }));
jest.mock('../../../contexts/ThemeContext', () => ({ useTheme: () => ({ theme: 'light', toggle: jest.fn() }) }));
jest.mock('../../auth/UserProfile', () => () => <div data-testid="user-profile" />);

function renderSidebar() {
  return render(
    <MemoryRouter>
      <Sidebar isOpen isCollapsed={false} onClose={jest.fn()} onToggleCollapse={jest.fn()} onMenuClick={jest.fn()} />
    </MemoryRouter>
  );
}

describe('Sidebar navigation', () => {
  afterEach(() => {
    mockPermissions = [];
    mockIsSuperUser = false;
  });

  it('shows only Dashboard for a user with no permissions', () => {
    renderSidebar();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.queryByText('Anela')).not.toBeInTheDocument();
    expect(screen.queryByText('Finance')).not.toBeInTheDocument();
    expect(screen.queryByText('Administrace')).not.toBeInTheDocument();
  });

  it('shows Administrace only for a user with administration.read', () => {
    mockPermissions = ['admin.administration.read'];
    renderSidebar();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Administrace')).toBeInTheDocument();
    expect(screen.queryByText('Finance')).not.toBeInTheDocument();
    expect(screen.queryByText('Produkty')).not.toBeInTheDocument();
    expect(screen.queryByText('Marketing')).not.toBeInTheDocument();
    expect(screen.queryByText('Nákup')).not.toBeInTheDocument();
    expect(screen.queryByText('Výroba')).not.toBeInTheDocument();
    expect(screen.queryByText('Sklad')).not.toBeInTheDocument();
  });

  it('shows Finance when user has financial_overview.read', () => {
    mockPermissions = ['finance.financial_overview.read'];
    renderSidebar();
    expect(screen.getByText('Finance')).toBeInTheDocument();
    expect(screen.queryByText('Administrace')).not.toBeInTheDocument();
  });

  it('shows everything for super_user', () => {
    mockIsSuperUser = true;
    renderSidebar();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Anela')).toBeInTheDocument();
    expect(screen.getByText('Finance')).toBeInTheDocument();
    expect(screen.getByText('Administrace')).toBeInTheDocument();
  });

  it('Anela section appears before Administrace when both are visible', () => {
    mockPermissions = ['anela.meetings.read', 'admin.administration.read'];
    renderSidebar();
    const buttons = screen.getAllByRole('button');
    const anelaIdx = buttons.findIndex(b => b.textContent?.includes('Anela'));
    const administraceIdx = buttons.findIndex(b => b.textContent?.includes('Administrace'));
    expect(anelaIdx).toBeGreaterThanOrEqual(0);
    expect(administraceIdx).toBeGreaterThanOrEqual(0);
    expect(anelaIdx).toBeLessThan(administraceIdx);
  });

  it('every rendered menu item key exists in ACCESS_ROUTES', () => {
    mockIsSuperUser = true;
    renderSidebar();

    const matrixKeys = Object.keys(ACCESS_ROUTES);
    const collectedKeys: string[] = [];

    // Section headers have no type attribute; utility buttons (mobile menu,
    // changelog, collapse) have explicit type="button". Expand each section
    // one at a time, collect keys, then collapse before moving to the next.
    const getSectionButtons = () =>
      screen.getAllByRole('button').filter(
        b => !b.getAttribute('type') && !b.getAttribute('data-menu-key')
      );

    for (const btn of getSectionButtons()) {
      // Expand section
      fireEvent.click(btn);

      // Collect link hrefs rendered inside expanded section
      screen.getAllByRole('link').forEach(l => {
        const href = l.getAttribute('href');
        if (href && href !== '/') collectedKeys.push(href);
      });

      // Collect data-menu-key from external-link buttons
      screen.getAllByRole('button').forEach(b => {
        const key = b.getAttribute('data-menu-key');
        if (key && key.startsWith('#')) collectedKeys.push(key);
      });

      // Collapse section again (click toggles it closed)
      fireEvent.click(btn);
    }

    const uniqueKeys = [...new Set(collectedKeys)];
    expect(uniqueKeys.length).toBeGreaterThan(0);
    for (const k of uniqueKeys) {
      expect(matrixKeys).toContain(k);
    }
  });
});

// TODO(authz): consider scraping App.tsx for <Route path="..."> to validate
// every non-virtual MenuPath.Key resolves to a real React route. Manual review
// suffices for now.
