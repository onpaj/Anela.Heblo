import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import LeafletGenerateTab from '../LeafletGenerateTab';
import { getAuthenticatedApiClient } from '../../../api/client';
import { ErrorCodes, GenerateLeafletResponse } from '../../../api/generated/api-client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

jest.mock('../../../api/hooks/useLeaflet', () => ({
  useSubmitLeafletFeedbackMutation: () => ({
    mutate: jest.fn(),
    isPending: false,
    isError: false,
  }),
}));

jest.mock('react-markdown', () => ({
  __esModule: true,
  default: ({ children }: { children: string }) => <div>{children}</div>,
}));

let mockGenerate: jest.Mock;

beforeEach(() => {
  jest.clearAllMocks();
  mockGenerate = jest.fn();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    leaflet_Generate: mockGenerate,
  });
});

async function fillAndSubmit() {
  fireEvent.change(screen.getByLabelText('Téma'), { target: { value: 'Bisabolol' } });
  fireEvent.click(screen.getByRole('button', { name: 'Vygenerovat leták' }));
}

describe('LeafletGenerateTab', () => {
  it('shows the insufficient knowledge banner when the API rejects with LeafletEmptyRetrieval', async () => {
    const errorResponse = new GenerateLeafletResponse({
      success: false,
      errorCode: ErrorCodes.LeafletEmptyRetrieval,
    });
    mockGenerate.mockRejectedValue(errorResponse);

    render(<LeafletGenerateTab />);
    await fillAndSubmit();

    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.');
    expect(banner.className).toContain('bg-amber-100');
  });

  it('shows the transient failure banner for a generic error', async () => {
    mockGenerate.mockRejectedValue(new Error('network down'));

    render(<LeafletGenerateTab />);
    await fillAndSubmit();

    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Generování selhalo. Zkuste to prosím znovu.');
    expect(banner.className).toContain('bg-red-100');
  });
});
