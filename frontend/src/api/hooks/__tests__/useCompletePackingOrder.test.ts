import { completePackingOrder } from '../useCompletePackingOrder';
import { getAuthenticatedApiClient } from '../../client';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    baseUrl: 'http://api',
    http: { fetch: mockFetch },
  });
});

describe('completePackingOrder', () => {
  it('POSTs to the packing/complete endpoint with the encoded order code', async () => {
    mockFetch.mockResolvedValue({ json: async () => ({ success: true }) });

    await completePackingOrder('25/0001');

    expect(mockFetch).toHaveBeenCalledWith(
      'http://api/api/packaging/orders/25%2F0001/packing/complete',
      { method: 'POST' }
    );
  });

  it('throws a friendly message when the server reports failure', async () => {
    mockFetch.mockResolvedValue({
      json: async () => ({ success: false, errorCode: 'PackingCompletionFailed' }),
    });

    await expect(completePackingOrder('250001')).rejects.toThrow(
      'Nepodařilo se dokončit balení objednávky.'
    );
  });
});
