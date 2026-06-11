import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LeafletGeneratorPage from '../LeafletGeneratorPage';

jest.mock('../LeafletGenerateTab', () => ({
  __esModule: true,
  default: () => <div data-testid="generate-tab-content">GenerateTab</div>,
}));

jest.mock('../LeafletDocumentsTab', () => ({
  __esModule: true,
  default: ({ canDelete }: { canDelete: boolean }) => (
    <div data-testid="documents-tab-content">DocumentsTab canDelete={String(canDelete)}</div>
  ),
}));

jest.mock('../LeafletUploadTab', () => ({
  __esModule: true,
  default: () => <div data-testid="upload-tab-content">UploadTab</div>,
}));

let mockHasPermission: (perm: string) => boolean = () => false;
jest.mock('../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));

function setCanUpload(value: boolean) {
  mockHasPermission = (p) => value && p === 'marketing.leaflet.write';
}

function renderPage() {
  return render(
    <MemoryRouter>
      <LeafletGeneratorPage />
    </MemoryRouter>
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  setCanUpload(false);
});

describe('LeafletGeneratorPage', () => {
  it('renders heading Generátor letáků', () => {
    setCanUpload(false);
    renderPage();
    expect(screen.getByRole('heading', { level: 1, name: 'Generátor letáků' })).toBeInTheDocument();
  });

  it('shows Generovat and Dokumenty tabs when user cannot upload', () => {
    setCanUpload(false);
    renderPage();
    expect(screen.getByRole('button', { name: 'Generovat' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Dokumenty' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nahrát soubor' })).not.toBeInTheDocument();
  });

  it('shows Nahrát soubor tab when user has upload permission', () => {
    setCanUpload(true);
    renderPage();
    expect(screen.getByRole('button', { name: 'Nahrát soubor' })).toBeInTheDocument();
  });

  it('renders generate tab content by default', () => {
    setCanUpload(false);
    renderPage();
    expect(screen.getByTestId('generate-tab-content')).toBeInTheDocument();
  });

  it('switches to documents tab when Dokumenty is clicked', () => {
    setCanUpload(false);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Dokumenty' }));
    expect(screen.getByTestId('documents-tab-content')).toBeInTheDocument();
    expect(screen.queryByTestId('generate-tab-content')).not.toBeInTheDocument();
  });

  it('passes canDelete=false to documents tab when user cannot upload', () => {
    setCanUpload(false);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Dokumenty' }));
    expect(screen.getByTestId('documents-tab-content')).toHaveTextContent('canDelete=false');
  });

  it('passes canDelete=true to documents tab when user can upload', () => {
    setCanUpload(true);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Dokumenty' }));
    expect(screen.getByTestId('documents-tab-content')).toHaveTextContent('canDelete=true');
  });

  it('switches to upload tab when Nahrát soubor is clicked', () => {
    setCanUpload(true);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Nahrát soubor' }));
    expect(screen.getByTestId('upload-tab-content')).toBeInTheDocument();
    expect(screen.queryByTestId('generate-tab-content')).not.toBeInTheDocument();
  });
});
