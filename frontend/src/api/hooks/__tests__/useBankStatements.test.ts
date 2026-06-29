import { renderHook, waitFor } from '@testing-library/react';
import { useBankStatementAccounts } from '../useBankStatements';
import { mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useBankStatements - Account Listing', () => {
    let mockClient: {
        bankStatements_GetAccounts: jest.Mock;
        bankStatements_GetBankStatements: jest.Mock;
        bankStatements_ImportStatements: jest.Mock;
    };

    beforeEach(() => {
        jest.clearAllMocks();
        mockClient = {
            bankStatements_GetAccounts: jest.fn(),
            bankStatements_GetBankStatements: jest.fn(),
            bankStatements_ImportStatements: jest.fn(),
        };
        mockAuthenticatedApiClient(mockClient);
    });

    describe('useBankStatementAccounts', () => {
        it('should return Comgate CZK account from backend', async () => {
            mockClient.bankStatements_GetAccounts.mockResolvedValue([
                { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' }
            ]);

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
            mockClient.bankStatements_GetAccounts.mockResolvedValue([
                { name: 'ShoptetPay-CZK', accountNumber: '', provider: 'ShoptetPay', currency: 'CZK' }
            ]);

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
            mockClient.bankStatements_GetAccounts.mockResolvedValue([
                { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' },
                { name: 'ComgateEUR', accountNumber: '2501837465/2010', provider: 'Comgate', currency: 'EUR' },
                { name: 'ShoptetPay-CZK', accountNumber: '', provider: 'ShoptetPay', currency: 'CZK' },
            ]);

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
            mockClient.bankStatements_GetAccounts.mockResolvedValue([
                { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' },
                { name: 'ShoptetPay-CZK', accountNumber: '', provider: 'ShoptetPay', currency: 'CZK' },
            ]);

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

        it('should call bankStatements_GetAccounts on the api client', async () => {
            mockClient.bankStatements_GetAccounts.mockResolvedValue([]);

            const { wrapper } = createQueryClientWrapper();
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            await waitFor(() => expect(result.current.isSuccess).toBe(true));

            expect(mockClient.bankStatements_GetAccounts).toHaveBeenCalledTimes(1);
            expect(mockClient.bankStatements_GetAccounts).toHaveBeenCalledWith();
        });
    });
});
