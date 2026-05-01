import React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import LeafletGeneratorPage from '../LeafletGeneratorPage';
import { getAuthenticatedApiClient } from '../../../api/client';

// Stub that always exposes a clickable Submit button and an input to set the topic
jest.mock('../LeafletForm', () => ({
    __esModule: true,
    default: ({ onSubmit, onTopicChange }: any) => (
        <div>
            <input
                data-testid="topic-input"
                onChange={(e) => onTopicChange(e.target.value)}
            />
            <button onClick={onSubmit}>Submit</button>
        </div>
    ),
}));

jest.mock('../LeafletResult', () => ({
    __esModule: true,
    default: ({ content, onRegenerate }: any) =>
        content ? (
            <div data-testid="result">
                {content}
                <button onClick={onRegenerate}>Regenerate</button>
            </div>
        ) : null,
}));

jest.mock('../../../api/client', () => ({
    getAuthenticatedApiClient: jest.fn(),
}));

const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.Mock;

/**
 * Set a non-empty topic so LeafletForm (real) would enable submit.
 * In our stub the button is always clickable, but we set topic so
 * the page state is consistent with realistic usage.
 */
async function setTopic(value = 'Test topic') {
    const input = screen.getByTestId('topic-input');
    await userEvent.type(input, value);
}

async function clickSubmit() {
    const button = screen.getByRole('button', { name: 'Submit' });
    await userEvent.click(button);
}

describe('LeafletGeneratorPage', () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    it('renders heading Generátor letáků on mount', () => {
        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: jest.fn().mockResolvedValue({ content: '' }),
        });

        render(<LeafletGeneratorPage />);

        expect(
            screen.getByRole('heading', { level: 1, name: 'Generátor letáků' })
        ).toBeInTheDocument();
    });

    it('shows loading skeleton while request is pending', async () => {
        let resolveGenerate!: (value: any) => void;
        const pendingPromise = new Promise<any>((resolve) => {
            resolveGenerate = resolve;
        });

        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: jest.fn().mockReturnValue(pendingPromise),
        });

        render(<LeafletGeneratorPage />);
        await setTopic();
        await userEvent.click(screen.getByRole('button', { name: 'Submit' }));

        await waitFor(() => {
            expect(document.querySelector('.animate-pulse')).toBeInTheDocument();
        });

        // Resolve to avoid unhandled promise rejection
        act(() => {
            resolveGenerate({ content: 'done' });
        });
    });

    it('displays generated content on success', async () => {
        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: jest.fn().mockResolvedValue({ content: 'Hello' }),
        });

        render(<LeafletGeneratorPage />);
        await setTopic();
        await clickSubmit();

        await waitFor(() => {
            expect(screen.getByTestId('result')).toHaveTextContent('Hello');
        });
    });

    it('shows insufficient-knowledge banner on 422', async () => {
        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: jest.fn().mockRejectedValue({
                response: { status: 422, data: { detail: 'Specific msg' } },
            }),
        });

        render(<LeafletGeneratorPage />);
        await setTopic();
        await clickSubmit();

        await waitFor(() => {
            const alert = screen.getByRole('alert');
            expect(alert).toBeInTheDocument();
            expect(alert).toHaveTextContent('Specific msg');
        });
    });

    it('shows transient error banner on 502', async () => {
        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: jest.fn().mockRejectedValue({
                response: { status: 502 },
            }),
        });

        render(<LeafletGeneratorPage />);
        await setTopic();
        await clickSubmit();

        await waitFor(() => {
            const alert = screen.getByRole('alert');
            expect(alert).toBeInTheDocument();
            expect(alert).toHaveTextContent('Generování selhalo');
        });
    });

    it('clears banner on next successful submit', async () => {
        const leafletGenerate = jest
            .fn()
            .mockRejectedValueOnce({ response: { status: 422, data: { detail: 'Error!' } } })
            .mockResolvedValueOnce({ content: 'Success result' });

        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: leafletGenerate,
        });

        render(<LeafletGeneratorPage />);
        await setTopic();

        // First submit — 422 error
        await clickSubmit();

        await waitFor(() => {
            expect(screen.getByRole('alert')).toBeInTheDocument();
        });

        // Second submit — success
        await clickSubmit();

        await waitFor(() => {
            expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
    });

    it('regenerate button calls API again', async () => {
        const leafletGenerate = jest
            .fn()
            .mockResolvedValue({ content: 'Generated text' });

        mockGetAuthenticatedApiClient.mockReturnValue({
            leaflet_Generate: leafletGenerate,
        });

        render(<LeafletGeneratorPage />);
        await setTopic();

        // First submit
        await clickSubmit();

        await waitFor(() => {
            expect(screen.getByTestId('result')).toBeInTheDocument();
        });

        // Click Regenerate from the result stub
        await userEvent.click(screen.getByRole('button', { name: 'Regenerate' }));

        await waitFor(() => {
            expect(leafletGenerate).toHaveBeenCalledTimes(2);
        });
    });
});
