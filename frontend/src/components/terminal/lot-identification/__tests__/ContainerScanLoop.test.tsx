import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ContainerScanLoop from '../ContainerScanLoop';
import { ErrorCodes } from '../../../../types/errors';

const mockMutate = jest.fn();
jest.mock('../../../../api/hooks/useMaterialContainers', () => ({
  useCreateMaterialContainers: () => ({
    mutate: mockMutate,
    isPending: false,
    reset: jest.fn(),
  }),
}));

beforeEach(() => mockMutate.mockReset());

const renderLoop = (path: string) => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route
            path="/terminal/lot-identification/freeform/:material/lot/:lot/scan"
            element={<ContainerScanLoop mode="freeform" />}
          />
          <Route
            path="/terminal/lot-identification/po/:id/line/:lineId/material/:material/lot/:lot/scan"
            element={<ContainerScanLoop mode="po" />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
};

test('header shows sticky material + lot context', () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  expect(screen.getByText('MAT001')).toBeInTheDocument();
  expect(screen.getByText('L1')).toBeInTheDocument();
});

test('valid scan calls mutate with correct payload', async () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'M00000001' } });
  fireEvent.submit(screen.getByRole('form'));
  await waitFor(() => {
    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [expect.objectContaining({ code: 'M00000001', materialCode: 'MAT001', lotCode: 'L1' })],
      }),
      expect.anything(),
    );
  });
});

test('po mode: valid scan sends materialCode from URL', async () => {
  renderLoop('/terminal/lot-identification/po/1/line/10/material/MAT001/lot/L1/scan');
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'M00000002' } });
  fireEvent.submit(screen.getByRole('form'));
  await waitFor(() => {
    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [expect.objectContaining({ code: 'M00000002', materialCode: 'MAT001', lotCode: 'L1' })],
      }),
      expect.anything(),
    );
  });
});

test('unknown container code shows generate-first message and does not increment count', async () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'M00000003' } });
  fireEvent.submit(screen.getByRole('form'));
  await waitFor(() => expect(mockMutate).toHaveBeenCalled());

  const onSuccess = mockMutate.mock.calls[0][1].onSuccess;
  act(() => {
    onSuccess({ success: false, errorCode: ErrorCodes.UnknownMaterialContainerCode });
  });

  expect(screen.getByRole('alert')).toHaveTextContent(
    'Neznámý štítek – nejprve jej vygenerujte v aplikaci.',
  );
  expect(screen.getByText('Naskenováno:')).toHaveTextContent('Naskenováno: 0');
});

test('invalid format does not call mutate', async () => {
  renderLoop('/terminal/lot-identification/freeform/MAT001/lot/L1/scan');
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'BADCODE' } });
  fireEvent.submit(screen.getByRole('form'));
  await new Promise((r) => setTimeout(r, 50));
  expect(mockMutate).not.toHaveBeenCalled();
});
