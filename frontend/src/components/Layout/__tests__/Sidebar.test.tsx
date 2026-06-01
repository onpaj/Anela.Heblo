import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from '../Sidebar';

jest.mock('../../../auth/useAuth', () => ({
  useAuth: () => ({
    getUserInfo: () => ({ roles: [] }),
    isAuthenticated: false,
    login: jest.fn(),
    logout: jest.fn(),
    getStoredUserInfo: jest.fn().mockReturnValue(null),
  }),
}));
jest.mock('../../../auth/mockAuth', () => ({
  useMockAuth: () => ({
    getUserInfo: () => null,
    isAuthenticated: false,
    login: jest.fn(),
    logout: jest.fn(),
    getStoredUserInfo: jest.fn().mockReturnValue(null),
  }),
  shouldUseMockAuth: () => false,
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
  it('shows "Anela" group (not "Personální")', () => {
    renderSidebar();
    expect(screen.getByText('Anela')).toBeInTheDocument();
    expect(screen.queryByText('Personální')).not.toBeInTheDocument();
  });

  it('does not show "Meeting Tasks" label; shows "Anela" group button', () => {
    renderSidebar();
    expect(screen.queryByText('Meeting Tasks')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Anela/i })).toBeInTheDocument();
  });

  // Finance is role-gated (requires finance_reader role) and won't appear with empty roles mock.
  // Instead we assert ordering relative to "Administrace" which is always visible.
  it('"Anela" group appears before other groups like Administrace', () => {
    renderSidebar();
    const buttons = screen.getAllByRole('button');
    const anelaIdx = buttons.findIndex(b => b.textContent?.includes('Anela'));
    const administraceIdx = buttons.findIndex(b => b.textContent?.includes('Administrace'));
    expect(anelaIdx).toBeGreaterThanOrEqual(0);
    expect(administraceIdx).toBeGreaterThanOrEqual(0);
    expect(anelaIdx).toBeLessThan(administraceIdx);
  });
});
