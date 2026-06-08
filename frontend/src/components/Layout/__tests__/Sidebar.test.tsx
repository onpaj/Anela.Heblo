import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from '../Sidebar';

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
    mockPermissions = ['administration.read'];
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
    mockPermissions = ['financial_overview.read'];
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
    mockPermissions = ['meetings.read', 'administration.read'];
    renderSidebar();
    const buttons = screen.getAllByRole('button');
    const anelaIdx = buttons.findIndex(b => b.textContent?.includes('Anela'));
    const administraceIdx = buttons.findIndex(b => b.textContent?.includes('Administrace'));
    expect(anelaIdx).toBeGreaterThanOrEqual(0);
    expect(administraceIdx).toBeGreaterThanOrEqual(0);
    expect(anelaIdx).toBeLessThan(administraceIdx);
  });
});
