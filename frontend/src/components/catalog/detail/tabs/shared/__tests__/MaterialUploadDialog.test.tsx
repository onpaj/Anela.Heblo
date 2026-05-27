import { render, screen, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import MaterialUploadDialog from '../MaterialUploadDialog';
import * as hooks from '../../../../../../api/hooks/useCatalogDocuments';

jest.mock('../../../../../../api/hooks/useCatalogDocuments');

const mockUseMaterialDocumentTypes = hooks.useMaterialDocumentTypes as jest.MockedFunction<typeof hooks.useMaterialDocumentTypes>;
const mockUseUploadMaterialDocument = hooks.useUploadMaterialDocument as jest.MockedFunction<typeof hooks.useUploadMaterialDocument>;

const createWrapper = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
};

const baseDocTypes = [
  { code: 'COA', label: 'Certificate of Analysis', lotRequired: true },
  { code: 'SDS', label: 'Safety Data Sheet', lotRequired: false },
];

describe('MaterialUploadDialog', () => {
  beforeEach(() => {
    mockUseMaterialDocumentTypes.mockReturnValue({
      data: { success: true, documentTypes: baseDocTypes },
      isLoading: false,
      error: null,
    } as any);
    mockUseUploadMaterialDocument.mockReturnValue({
      mutate: jest.fn(),
      isPending: false,
      isError: false,
    } as any);
  });

  it('shows lot field when lotRequired type is selected', () => {
    render(
      <MaterialUploadDialog isOpen={true} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'COA' } });
    expect(screen.getByLabelText(/Šarže/i)).toBeInTheDocument();
  });

  it('hides lot field when lotRequired is false', () => {
    render(
      <MaterialUploadDialog isOpen={true} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'SDS' } });
    expect(screen.queryByLabelText(/Šarže/i)).not.toBeInTheDocument();
  });

  it('collapses form fields when uploadAsIs is checked', () => {
    render(
      <MaterialUploadDialog isOpen={true} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );

    fireEvent.click(screen.getByLabelText(/Nahrát beze změny názvu/i));
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('renders nothing when isOpen is false', () => {
    const { container } = render(
      <MaterialUploadDialog isOpen={false} productCode="MAT001" onClose={() => {}} />,
      { wrapper: createWrapper() }
    );
    expect(container).toBeEmptyDOMElement();
  });
});
