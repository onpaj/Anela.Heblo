import { renderHook, waitFor } from '@testing-library/react';
import { useBankStatementAccounts } from '../useBankStatements';
import { getAuthenticatedApiClient } from '../../client';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useBankStatements - Account Listing', () => {
    let mockFetch: jest.Mock;
    let mockClient: any;

    beforeEach(() => {
        const mock = createMockApiClient();
        mockClient = mock.mockClient;
        mockFetch = mock.mockFetch;
        mockAuthenticatedApiClient(mockClient);
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe('useBankStatementAccounts', () => {
        it('should return Comgate CZK account from backend', async () => {
            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve([
                    { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' }
                ])
            });

            const { wrapper } = createQueryClientWrapper();
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const accounts = result.current.data!;
            expect(accounts).toHaveLength(1);
            expect(accounts[0].value).toBe('ComgateCZK');
            expect(accounts[0].label).toBe('ComgateCZK (Comgate)');
            expect(accounts[0].accountNumber).toBe('2301495165/2010');
            expect(accounts[0].provider).toBe('Comgate');
            expect(accounts[0].currency).toBe('CZK');
        });

        it('should return ShoptetPay account from backend', async () => {
            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve([
                    { name: 'ShoptetPay-CZK', accountNumber: '', provider: 'ShoptetPay', currency: 'CZK' }
                ])
            });

            const { wrapper } = createQueryClientWrapper();
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const accounts = result.current.data!;
            expect(accounts).toHaveLength(1);
            expect(accounts[0].value).toBe('ShoptetPay-CZK');
            expect(accounts[0].label).toBe('ShoptetPay-CZK (ShoptetPay)');
            expect(accounts[0].provider).toBe('ShoptetPay');
            expect(accounts[0].currency).toBe('CZK');
        });

        it('should return all configured accounts including ShoptetPay', async () => {
            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve([
                    { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' },
                    { name: 'ComgateEUR', accountNumber: '2501837465/2010', provider: 'Comgate', currency: 'EUR' },
                    { name: 'ShoptetPay-CZK', accountNumber: '', provider: 'ShoptetPay', currency: 'CZK' },
                ])
            });

            const { wrapper } = createQueryClientWrapper();
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            const accounts = result.current.data!;
            expect(accounts).toHaveLength(3);

            const names = accounts.map(a => a.value);
            expect(names).toContain('ComgateCZK');
            expect(names).toContain('ComgateEUR');
            expect(names).toContain('ShoptetPay-CZK');
        });

        it('should expose value and label for every account', async () => {
            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve([
                    { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' },
                    { name: 'ShoptetPay-CZK', accountNumber: '', provider: 'ShoptetPay', currency: 'CZK' },
                ])
            });

            const { wrapper } = createQueryClientWrapper();
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            result.current.data!.forEach(account => {
                expect(account).toHaveProperty('value');
                expect(account).toHaveProperty('label');
                expect(account).toHaveProperty('accountNumber');
                expect(account).toHaveProperty('provider');
                expect(account).toHaveProperty('currency');
                expect(account.label).toContain(account.value);
                expect(account.label).toContain(account.provider);
            });
        });

        it('should call /api/bank-statements/accounts endpoint', async () => {
            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve([])
            });

            const { wrapper } = createQueryClientWrapper();
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(mockFetch).toHaveBeenCalledWith(
                expect.stringContaining('/api/bank-statements/accounts'),
                expect.any(Object)
            );
        });
    });
});
