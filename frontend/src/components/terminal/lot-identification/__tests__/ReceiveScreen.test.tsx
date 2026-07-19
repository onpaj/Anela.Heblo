import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ReceiveScreen from '../ReceiveScreen';
import { ScanProvider } from '../../shell/ScanProvider';
import { ErrorCodes } from '../../../../types/errors';

const mockMutate = jest.fn();
jest.mock('../../../../api/hooks/useMaterialContainers', () => ({
  useCreateMaterialContainers: () => ({ mutate: mockMutate, isPending: false }),
  useLastUsedLotForMaterial: () => ({ data: undefined }),
}));

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

// Stub the catalog autocomplete: clicking "pick-material" selects a fixed material.
jest.mock('../../../common/CatalogAutocomplete', () => ({
  __esModule: true,
  default: ({ onSelect }: { onSelect: (item: { productCode: string; productName: string } | null) => void }) => (
    <button onClick={() => onSelect({ productCode: 'MAT001', productName: 'Olive Oil' })}>pick-material</button>
  ),
}));

beforeEach(() => {
  mockMutate.mockReset();
  mockNavigate.mockReset();
});

const LOT_LABEL = 'Šarže (z etikety dodavatele)';
const SCAN_LABEL = 'Kód kontejneru (Mxxxxxxxx)';

const client = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

const renderFreeform = () =>
  render(
    <QueryClientProvider client={client()}>
      <MemoryRouter initialEntries={['/terminal/lot-identification/freeform']}>
        <ScanProvider>
          <Routes>
            <Route path="/terminal/lot-identification/freeform" element={<ReceiveScreen mode="freeform" />} />
          </Routes>
        </ScanProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );

const renderPo = () =>
  render(
    <QueryClientProvider client={client()}>
      <MemoryRouter initialEntries={['/terminal/lot-identification/po/1/line/10/material/MAT001']}>
        <ScanProvider>
          <Routes>
            <Route
              path="/terminal/lot-identification/po/:id/line/:lineId/material/:material"
              element={<ReceiveScreen mode="po" />}
            />
          </Routes>
        </ScanProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );

const submit = (input: HTMLElement, value: string) => {
  fireEvent.change(input, { target: { value } });
  fireEvent.submit((input as HTMLInputElement).form!);
};

const countMatcher = (n: number) => (_: string, el: Element | null) =>
  el?.tagName === 'P' && new RegExp(`Naskenováno:\\s*${n}`).test(el.textContent ?? '');

test('freeform starts on material step; selecting a material enables the lot field', () => {
  renderFreeform();
  expect(screen.getByText('pick-material')).toBeInTheDocument();
  expect(screen.getByRole('textbox', { name: LOT_LABEL })).toBeDisabled();

  fireEvent.click(screen.getByText('pick-material'));

  expect(screen.getByTestId('receive-material-code')).toHaveTextContent('MAT001');
  expect(screen.getByRole('textbox', { name: LOT_LABEL })).toBeEnabled();
  expect(screen.getByRole('textbox', { name: SCAN_LABEL })).toBeDisabled();
});

test('po starts with the material locked and the lot field active', () => {
  renderPo();
  expect(screen.getByTestId('receive-material-code')).toHaveTextContent('MAT001');
  expect(screen.getByRole('textbox', { name: LOT_LABEL })).toBeEnabled();
  expect(screen.getByRole('textbox', { name: SCAN_LABEL })).toBeDisabled();
  // Material section shows no autocomplete (it's locked).
  expect(screen.queryByText('pick-material')).not.toBeInTheDocument();
});

test('confirming the lot activates the scan field', () => {
  renderPo();
  submit(screen.getByRole('textbox', { name: LOT_LABEL }), 'L1');
  expect(screen.getByRole('textbox', { name: SCAN_LABEL })).toBeEnabled();
});

test('a valid scan posts the correct payload including the PO line id', async () => {
  renderPo();
  submit(screen.getByRole('textbox', { name: LOT_LABEL }), 'L1');
  submit(screen.getByRole('textbox', { name: SCAN_LABEL }), 'M00000001');
  await waitFor(() => {
    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [
          expect.objectContaining({
            code: 'M00000001',
            materialCode: 'MAT001',
            lotCode: 'L1',
            purchaseOrderLineId: 10,
          }),
        ],
      }),
      expect.anything(),
    );
  });
});

test('a saved scan appears in the box list and increments the counter', async () => {
  renderPo();
  submit(screen.getByRole('textbox', { name: LOT_LABEL }), 'L1');
  submit(screen.getByRole('textbox', { name: SCAN_LABEL }), 'M00000001');
  await waitFor(() => expect(mockMutate).toHaveBeenCalled());

  const onSuccess = mockMutate.mock.calls[0][1].onSuccess;
  act(() => onSuccess({ success: true }));

  expect(screen.getByText('Uloženo')).toBeInTheDocument();
  expect(screen.getByText(countMatcher(1))).toBeInTheDocument();
});

test('a duplicate code renders an alert row and does not count as saved', async () => {
  renderPo();
  submit(screen.getByRole('textbox', { name: LOT_LABEL }), 'L1');
  submit(screen.getByRole('textbox', { name: SCAN_LABEL }), 'M00000001');
  await waitFor(() => expect(mockMutate).toHaveBeenCalled());

  const onSuccess = mockMutate.mock.calls[0][1].onSuccess;
  act(() =>
    onSuccess({
      success: false,
      errorCode: ErrorCodes.MaterialContainerCodeExists,
      params: { Status: 'Assigned', MaterialCode: 'MAT001', LotCode: 'L9' },
    }),
  );

  expect(screen.getByRole('alert')).toHaveTextContent(/je již přiřazen/);
  expect(screen.getByText(countMatcher(0))).toBeInTheDocument();
});

test('an invalid code format is rejected client-side without hitting the backend', () => {
  renderPo();
  submit(screen.getByRole('textbox', { name: LOT_LABEL }), 'L1');
  submit(screen.getByRole('textbox', { name: SCAN_LABEL }), 'BADCODE');
  expect(mockMutate).not.toHaveBeenCalled();
  expect(screen.getByRole('alert')).toHaveTextContent(/Neplatný formát/);
});

test('Hotovo in freeform resets back to the material step', () => {
  renderFreeform();
  fireEvent.click(screen.getByText('pick-material'));
  submit(screen.getByRole('textbox', { name: LOT_LABEL }), 'L1');
  expect(screen.getByRole('textbox', { name: SCAN_LABEL })).toBeEnabled();

  fireEvent.click(screen.getByRole('button', { name: 'Hotovo' }));

  // Material combobox is back and the lot field is disabled again.
  expect(screen.getByText('pick-material')).toBeInTheDocument();
  expect(screen.getByRole('textbox', { name: LOT_LABEL })).toBeDisabled();
});

test('Hotovo in po navigates back to the order line list', () => {
  renderPo();
  fireEvent.click(screen.getByRole('button', { name: 'Hotovo' }));
  expect(mockNavigate).toHaveBeenCalledWith('/terminal/lot-identification/po/1');
});
