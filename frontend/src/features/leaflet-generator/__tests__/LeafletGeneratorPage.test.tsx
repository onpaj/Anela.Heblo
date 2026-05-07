import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LeafletGeneratorPage from '../LeafletGeneratorPage';
import * as useGenAiHooks from '../../../api/hooks/useGenAiUserPermission';

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

jest.mock('../../../api/hooks/useGenAiUserPermission', () => ({
  useGenAiUserPermission: jest.fn(),
}));

const mockUseGenAiUserPermission = useGenAiHooks.useGenAiUserPermission as jest.Mock;

function renderPage() {
  return render(
    <MemoryRouter>
      <LeafletGeneratorPage />
    </MemoryRouter>
  );
}

beforeEach(() => {
  jest.clearAllMocks();
});

describe('LeafletGeneratorPage', () => {
  it('renders heading Generátor letáků', () => {
    mockUseGenAiUserPermission.mockReturnValue(false);
    renderPage();
    expect(screen.getByRole('heading', { level: 1, name: 'Generátor letáků' })).toBeInTheDocument();
  });

  it('shows Generovat and Dokumenty tabs when user cannot upload', () => {
    mockUseGenAiUserPermission.mockReturnValue(false);
    renderPage();
    expect(screen.getByRole('button', { name: 'Generovat' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Dokumenty' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nahrát soubor' })).not.toBeInTheDocument();
  });

  it('shows Nahrát soubor tab when user has upload permission', () => {
    mockUseGenAiUserPermission.mockReturnValue(true);
    renderPage();
    expect(screen.getByRole('button', { name: 'Nahrát soubor' })).toBeInTheDocument();
  });

  it('renders generate tab content by default', () => {
    mockUseGenAiUserPermission.mockReturnValue(false);
    renderPage();
    expect(screen.getByTestId('generate-tab-content')).toBeInTheDocument();
  });

  it('switches to documents tab when Dokumenty is clicked', () => {
    mockUseGenAiUserPermission.mockReturnValue(false);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Dokumenty' }));
    expect(screen.getByTestId('documents-tab-content')).toBeInTheDocument();
    expect(screen.queryByTestId('generate-tab-content')).not.toBeInTheDocument();
  });

  it('passes canDelete=false to documents tab when user cannot upload', () => {
    mockUseGenAiUserPermission.mockReturnValue(false);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Dokumenty' }));
    expect(screen.getByTestId('documents-tab-content')).toHaveTextContent('canDelete=false');
  });

  it('passes canDelete=true to documents tab when user can upload', () => {
    mockUseGenAiUserPermission.mockReturnValue(true);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Dokumenty' }));
    expect(screen.getByTestId('documents-tab-content')).toHaveTextContent('canDelete=true');
  });

  it('switches to upload tab when Nahrát soubor is clicked', () => {
    mockUseGenAiUserPermission.mockReturnValue(true);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Nahrát soubor' }));
    expect(screen.getByTestId('upload-tab-content')).toBeInTheDocument();
    expect(screen.queryByTestId('generate-tab-content')).not.toBeInTheDocument();
  });
});
