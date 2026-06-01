import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LotEntryStep from '../LotEntryStep';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

jest.mock('../../../../api/hooks/useMaterialContainers', () => ({
  useLastUsedLotForMaterial: () => ({ data: { lotCode: 'LOT-2026-04' }, isLoading: false })
}));

beforeEach(() => mockNavigate.mockReset());

const renderFreeform = (path = '/terminal/lot-identification/freeform/MAT001/lot') => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/terminal/lot-identification/freeform/:material/lot" element={<LotEntryStep mode="freeform" />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

const renderPo = (path = '/terminal/lot-identification/po/1/line/10/material/MAT001/lot') => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/terminal/lot-identification/po/:id/line/:lineId/material/:material/lot" element={<LotEntryStep mode="po" />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

const renderStep = renderFreeform;

test('pre-fills lot input with last-used lot for the material', async () => {
  renderStep();
  const input = await screen.findByRole('textbox') as HTMLInputElement;
  await waitFor(() => expect(input.value).toBe('LOT-2026-04'));
});

test('on submit, navigates to scan-loop step with material+lot in URL', () => {
  renderStep();
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'LOT-2026-05' } });
  fireEvent.submit(screen.getByRole('form'));
  expect(mockNavigate).toHaveBeenCalledWith(
    '/terminal/lot-identification/freeform/MAT001/lot/LOT-2026-05/scan'
  );
});

test('po mode: navigates to scan-loop step with material in URL', () => {
  renderPo();
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'LOT-2026-05' } });
  fireEvent.submit(screen.getByRole('form'));
  expect(mockNavigate).toHaveBeenCalledWith(
    '/terminal/lot-identification/po/1/line/10/material/MAT001/lot/LOT-2026-05/scan'
  );
});
