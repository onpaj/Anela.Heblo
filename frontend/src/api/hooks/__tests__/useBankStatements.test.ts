import { renderHook, waitFor } from '@testing-library/react';
import { useBankStatementAccounts, GetBankStatementListResponse } from '../useBankStatements';
import { getAuthenticatedApiClient } from '../../client';
import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

// Mock the API client
jest.mock('../../client');

describe('useBankStatements - Account Mapping Logic', () => {
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
        it('should map account number "2301495165/2010" to "ComgateCZK"', async () => {
            // Arrange
            const mockData: GetBankStatementListResponse = {
                items: [
                    {
                        id: 1,
                        transferId: 'T001',
                        statementDate: '2024-01-01',
                        importDate: '2024-01-02',
                        account: '2301495165/2010',
                        currency: 'CZK',
                        itemCount: 10,
                        importResult: 'Success'
                    }
                ],
                totalCount: 1
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockData)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            const accounts = result.current.data;
            expect(accounts).toHaveLength(1);
            expect(accounts![0].accountNumber).toBe('2301495165/2010');
            expect(accounts![0].accountName).toBe('ComgateCZK');
            expect(accounts![0].value).toBe('ComgateCZK'); // Backend expects this value
            expect(accounts![0].label).toBe('ComgateCZK (2301495165/2010)');
        });

        it('should map account number "2501837465/2010" to "ComgateEUR"', async () => {
            // Arrange
            const mockData: GetBankStatementListResponse = {
                items: [
                    {
                        id: 2,
                        transferId: 'T002',
                        statementDate: '2024-01-01',
                        importDate: '2024-01-02',
                        account: '2501837465/2010',
                        currency: 'EUR',
                        itemCount: 5,
                        importResult: 'Success'
                    }
                ],
                totalCount: 1
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockData)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            const accounts = result.current.data;
            expect(accounts).toHaveLength(1);
            expect(accounts![0].accountNumber).toBe('2501837465/2010');
            expect(accounts![0].accountName).toBe('ComgateEUR');
            expect(accounts![0].value).toBe('ComgateEUR'); // Backend expects this value
            expect(accounts![0].label).toBe('ComgateEUR (2501837465/2010)');
        });

        it('should use account number as fallback for unknown accounts', async () => {
            // Arrange - Unknown account number
            const unknownAccount = '9999999999/9999';
            const mockData: GetBankStatementListResponse = {
                items: [
                    {
                        id: 3,
                        transferId: 'T003',
                        statementDate: '2024-01-01',
                        importDate: '2024-01-02',
                        account: unknownAccount,
                        currency: 'CZK',
                        itemCount: 3,
                        importResult: 'Success'
                    }
                ],
                totalCount: 1
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockData)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            const accounts = result.current.data;
            expect(accounts).toHaveLength(1);
            expect(accounts![0].accountNumber).toBe(unknownAccount);
            expect(accounts![0].accountName).toBe(unknownAccount); // Falls back to account number
            expect(accounts![0].value).toBe(unknownAccount); // Backend will receive account number
            expect(accounts![0].label).toBe(`${unknownAccount} (${unknownAccount})`);
        });

        it('should return correct AccountOption objects with all required properties', async () => {
            // Arrange - Multiple accounts
            const mockData: GetBankStatementListResponse = {
                items: [
                    {
                        id: 1,
                        transferId: 'T001',
                        statementDate: '2024-01-01',
                        importDate: '2024-01-02',
                        account: '2301495165/2010',
                        currency: 'CZK',
                        itemCount: 10,
                        importResult: 'Success'
                    },
                    {
                        id: 2,
                        transferId: 'T002',
                        statementDate: '2024-01-01',
                        importDate: '2024-01-02',
                        account: '2501837465/2010',
                        currency: 'EUR',
                        itemCount: 5,
                        importResult: 'Success'
                    }
                ],
                totalCount: 2
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockData)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            const accounts = result.current.data;
            expect(accounts).toHaveLength(2);

            // Verify all accounts have required properties
            accounts!.forEach(account => {
                expect(account).toHaveProperty('value');
                expect(account).toHaveProperty('label');
                expect(account).toHaveProperty('accountNumber');
                expect(account).toHaveProperty('accountName');

                // value and accountName should match
                expect(account.value).toBe(account.accountName);

                // label should contain both accountName and accountNumber
                expect(account.label).toContain(account.accountName);
                expect(account.label).toContain(account.accountNumber);
            });
        });

        it('should deduplicate accounts and sort by label', async () => {
            // Arrange - Duplicate accounts
            const mockData: GetBankStatementListResponse = {
                items: [
                    {
                        id: 1,
                        transferId: 'T001',
                        statementDate: '2024-01-01',
                        importDate: '2024-01-02',
                        account: '2501837465/2010', // EUR
                        currency: 'EUR',
                        itemCount: 5,
                        importResult: 'Success'
                    },
                    {
                        id: 2,
                        transferId: 'T002',
                        statementDate: '2024-01-02',
                        importDate: '2024-01-03',
                        account: '2301495165/2010', // CZK
                        currency: 'CZK',
                        itemCount: 10,
                        importResult: 'Success'
                    },
                    {
                        id: 3,
                        transferId: 'T003',
                        statementDate: '2024-01-03',
                        importDate: '2024-01-04',
                        account: '2301495165/2010', // Duplicate CZK
                        currency: 'CZK',
                        itemCount: 7,
                        importResult: 'Success'
                    }
                ],
                totalCount: 3
            };

            mockFetch.mockResolvedValue({
                ok: true,
                json: () => Promise.resolve(mockData)
            });

            const { wrapper } = createQueryClientWrapper();

            // Act
            const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

            // Assert
            await waitFor(() => {
                expect(result.current.isSuccess).toBe(true);
            });

            const accounts = result.current.data;

            // Should have only 2 unique accounts despite 3 statements
            expect(accounts).toHaveLength(2);

            // Should be sorted alphabetically by label
            // ComgateCZK comes before ComgateEUR
            expect(accounts![0].accountName).toBe('ComgateCZK');
            expect(accounts![1].accountName).toBe('ComgateEUR');
        });
    });
});
