import React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import IssuedInvoiceDetailModal from '../IssuedInvoiceDetailModal';
import * as useIssuedInvoicesHooks from '../../../api/hooks/useIssuedInvoices';
import * as useAsyncInvoiceImportHooks from '../../../api/hooks/useAsyncInvoiceImport';

// Mock the hooks
jest.mock('../../../api/hooks/useIssuedInvoices');
jest.mock('../../../api/hooks/useAsyncInvoiceImport');

describe('IssuedInvoiceDetailModal - Re-Import from Detail Modal', () => {
    let queryClient: QueryClient;
    const mockUseIssuedInvoiceDetail = jest.fn();
    const mockUseEnqueueInvoiceImport = jest.fn();
    const mockOnClose = jest.fn();
    const mockOnInvoiceUpdated = jest.fn();
    const mockOnJobStarted = jest.fn();
    const mockMutateAsync = jest.fn();

    beforeEach(() => {
        queryClient = new QueryClient({
            defaultOptions: {
                queries: { retry: false },
                mutations: { retry: false }
            }
        });

        // Setup default mocks
        mockUseIssuedInvoiceDetail.mockReturnValue({
            data: {
                invoice: {
                    id: 'INV-001',
                    invoiceDate: '2024-01-15',
                    price: 10000,
                    currency: 'CZK',
                    isSynced: false,
                    errorType: null
                }
            },
            isLoading: false,
            error: null
        });

        mockUseEnqueueInvoiceImport.mockReturnValue({
            mutateAsync: mockMutateAsync,
            isLoading: false
        });

        (useIssuedInvoicesHooks as any).useIssuedInvoiceDetail = mockUseIssuedInvoiceDetail;
        (useAsyncInvoiceImportHooks as any).useEnqueueInvoiceImport = mockUseEnqueueInvoiceImport;

        jest.clearAllMocks();
    });

    const renderComponent = (invoiceId: string = 'INV-001') => {
        return render(
            <QueryClientProvider client={queryClient}>
                <IssuedInvoiceDetailModal
                    invoiceId={invoiceId}
                    isOpen={true}
                    onClose={mockOnClose}
                    onInvoiceUpdated={mockOnInvoiceUpdated}
                    onJobStarted={mockOnJobStarted}
                />
            </QueryClientProvider>
        );
    };

    it('should enqueue job with invoiceId and currency when re-import is clicked', async () => {
        // Arrange
        mockMutateAsync.mockResolvedValue({ jobId: 'job-reimport-123' });

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act - Click re-import button
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - Should call mutateAsync with correct params
        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith(
                expect.objectContaining({
                    query: expect.objectContaining({
                        invoiceId: 'INV-001',
                        currency: 'CZK' // From invoice data
                    })
                })
            );
        });
    });

    it('should pass result jobId to onJobStarted callback', async () => {
        // Arrange
        const testJobId = 'job-test-456';
        mockMutateAsync.mockResolvedValue({ jobId: testJobId });

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - onJobStarted should be called with jobId
        await waitFor(() => {
            expect(mockOnJobStarted).toHaveBeenCalledWith(testJobId);
        });
    });

    it('should close modal immediately after enqueue', async () => {
        // Arrange
        mockMutateAsync.mockResolvedValue({ jobId: 'job-close' });

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - onClose should be called
        await waitFor(() => {
            expect(mockOnClose).toHaveBeenCalled();
        });
    });

    it('should trigger onInvoiceUpdated callback for background refresh', async () => {
        // Arrange
        mockMutateAsync.mockResolvedValue({ jobId: 'job-refresh' });

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - onInvoiceUpdated should be called (after enqueue and close)
        await waitFor(() => {
            expect(mockOnInvoiceUpdated).toHaveBeenCalled();
        });
    });

    it('should disable re-import button during operation', async () => {
        // Arrange
        let resolveImport: (value: any) => void;
        const importPromise = new Promise((resolve) => {
            resolveImport = resolve;
        });
        mockMutateAsync.mockReturnValue(importPromise);

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act - Click re-import button
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - Button should be disabled during operation
        await waitFor(() => {
            expect(reimportButton).toBeDisabled();
        });

        // Resolve import
        resolveImport!({ jobId: 'job-done' });

        await waitFor(() => {
            expect(mockOnClose).toHaveBeenCalled();
        });
    });

    it('should use invoice currency for re-import request', async () => {
        // Arrange - EUR invoice
        mockUseIssuedInvoiceDetail.mockReturnValue({
            data: {
                invoice: {
                    id: 'INV-EUR-001',
                    invoiceDate: '2024-01-15',
                    price: 1000,
                    currency: 'EUR',
                    isSynced: false,
                    errorType: null
                }
            },
            isLoading: false,
            error: null
        });

        mockMutateAsync.mockResolvedValue({ jobId: 'job-eur' });

        renderComponent('INV-EUR-001');

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-EUR-001/i)).toBeInTheDocument();
        });

        // Act
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - Should use EUR currency
        await waitFor(() => {
            expect(mockMutateAsync).toHaveBeenCalledWith(
                expect.objectContaining({
                    query: expect.objectContaining({
                        invoiceId: 'INV-EUR-001',
                        currency: 'EUR'
                    })
                })
            );
        });
    });

    it('should handle re-import error gracefully', async () => {
        // Arrange
        const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
        mockMutateAsync.mockRejectedValue(new Error('Import failed'));

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - Should log error and not crash
        await waitFor(() => {
            expect(consoleErrorSpy).toHaveBeenCalledWith('Reimport error:', expect.any(Error));
        });

        // Modal should NOT close on error
        expect(mockOnClose).not.toHaveBeenCalled();

        consoleErrorSpy.mockRestore();
    });

    it('should call all callbacks in correct order: enqueue -> onJobStarted -> onClose -> onInvoiceUpdated', async () => {
        // Arrange
        const callOrder: string[] = [];
        mockMutateAsync.mockResolvedValue({ jobId: 'job-order' });

        mockOnJobStarted.mockImplementation(() => {
            callOrder.push('onJobStarted');
        });

        mockOnClose.mockImplementation(() => {
            callOrder.push('onClose');
        });

        mockOnInvoiceUpdated.mockImplementation(() => {
            callOrder.push('onInvoiceUpdated');
            return Promise.resolve();
        });

        renderComponent();

        await waitFor(() => {
            expect(screen.getByText(/Detail faktury INV-001/i)).toBeInTheDocument();
        });

        // Act
        const reimportButton = screen.getByText(/Znovu importovat/i) || screen.getByRole('button', { name: /reimport/i });
        fireEvent.click(reimportButton);

        // Assert - Verify call order
        await waitFor(() => {
            expect(callOrder).toEqual(['onJobStarted', 'onClose', 'onInvoiceUpdated']);
        });
    });
});
