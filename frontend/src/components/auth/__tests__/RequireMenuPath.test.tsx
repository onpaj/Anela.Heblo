import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import RequireMenuPath from '../RequireMenuPath';

let mockPermissions: string[] = [];
let mockIsSuperUser = false;
let mockIsLoading = false;

jest.mock('../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: mockPermissions,
    isSuperUser: mockIsSuperUser,
    groups: [],
    isLoading: mockIsLoading,
    hasPermission: (p: string) => mockIsSuperUser || mockPermissions.includes(p),
  }),
}));

jest.mock('../../../auth/accessMatrix.generated', () => ({
  __esModule: true,
  ACCESS_ROUTES: {
    '/x': { permissions: ['a.b.read', 'c.d.read'] },
  },
}));

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/x" element={
          <RequireMenuPath path="/x"><div data-testid="ok">ok</div></RequireMenuPath>
        } />
        <Route path="/" element={<div data-testid="home">home</div>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RequireMenuPath', () => {
  afterEach(() => {
    mockPermissions = [];
    mockIsSuperUser = false;
    mockIsLoading = false;
  });

  it('renders children when all permissions held', () => {
    mockPermissions = ['a.b.read', 'c.d.read'];
    renderAt('/x');
    expect(screen.getByTestId('ok')).toBeInTheDocument();
  });

  it('redirects to dashboard when any permission missing', () => {
    mockPermissions = ['a.b.read'];
    renderAt('/x');
    expect(screen.queryByTestId('ok')).not.toBeInTheDocument();
    expect(screen.getByTestId('home')).toBeInTheDocument();
  });

  it('renders for super_user even without explicit permissions', () => {
    mockIsSuperUser = true;
    renderAt('/x');
    expect(screen.getByTestId('ok')).toBeInTheDocument();
  });

  it('renders nothing while permissions are loading', () => {
    mockIsLoading = true;
    renderAt('/x');
    expect(screen.queryByTestId('ok')).not.toBeInTheDocument();
    expect(screen.queryByTestId('home')).not.toBeInTheDocument();
  });
});
