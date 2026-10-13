import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { PackingStatsTile } from '../PackingStatsTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

const baseStats = {
  ordersBeingPackedCount: 3,
  ordersBeingProcessedCount: 7,
  ordersBeingPackedCountLastSync: null,
  totalOrdersPackedToday: 42,
  packedByPacker: [{ packerId: 'u1', packerName: 'Jana', orderCount: 20 }],
};

const renderTile = (data: any, targetUrl?: string) =>
  render(
    <BrowserRouter>
      <PackingStatsTile data={data} targetUrl={targetUrl} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('PackingStatsTile', () => {
  it('renders the error state and is not clickable', () => {
    const { container } = renderTile({ status: 'error', error: 'Nepodařilo se načíst data balení' });

    expect(screen.getByText('Nepodařilo se načíst data balení')).toBeInTheDocument();
    fireEvent.click(container.firstChild as HTMLElement);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('is not clickable when drillDown is absent', () => {
    const { container } = renderTile({ status: 'success', data: baseStats });

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).not.toContain('cursor-pointer');
    fireEvent.click(wrapper);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('is not clickable when drillDown.enabled is false', () => {
    const { container } = renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: false, filters: {}, tooltip: 'x' } },
      '/baleni',
    );

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).not.toContain('cursor-pointer');
    fireEvent.click(wrapper);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('navigates to targetUrl with no query string when filters is empty (current backend payload)', () => {
    const { container } = renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: true, filters: {}, tooltip: 'Přejít do modulu Balení' } },
      '/baleni',
    );

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).toContain('cursor-pointer');
    expect(wrapper.getAttribute('title')).toBe('Přejít do modulu Balení');

    fireEvent.click(wrapper);
    expect(mockNavigate).toHaveBeenCalledWith('/baleni');
  });

  it('navigates with query params when filters is non-empty', () => {
    const { container } = renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: true, filters: { packerId: 'u1' }, tooltip: 'Přejít do modulu Balení' } },
      '/baleni',
    );

    fireEvent.click(container.firstChild as HTMLElement);
    expect(mockNavigate).toHaveBeenCalledWith('/baleni?packerId=u1');
  });

  it('does not navigate when targetUrl is not supplied', () => {
    const { container } = renderTile({
      status: 'success',
      data: baseStats,
      drillDown: { enabled: true, filters: {}, tooltip: 'Přejít do modulu Balení' },
    });

    fireEvent.click(container.firstChild as HTMLElement);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('renders the packer breakdown list', () => {
    renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: true, filters: {}, tooltip: 'x' } },
      '/baleni',
    );

    expect(screen.getByText('Jana')).toBeInTheDocument();
    expect(screen.getByText('20')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
  });
});
