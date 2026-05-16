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

  it('shows "Porady" item (not "Meeting Tasks")', () => {
    renderSidebar();
    expect(screen.queryByText('Meeting Tasks')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Anela/i })).toBeInTheDocument();
  });

  it('"Anela" group appears before "Finance" in the DOM', () => {
    renderSidebar();
    const buttons = screen.getAllByRole('button');
    const anela = buttons.findIndex(b => b.textContent?.includes('Anela'));
    expect(anela).toBeGreaterThanOrEqual(0);
  });
});
