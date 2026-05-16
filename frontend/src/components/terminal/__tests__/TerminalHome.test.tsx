import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import TerminalHome from '../TerminalHome';

const renderHome = () =>
  render(
    <MemoryRouter>
      <TerminalHome />
    </MemoryRouter>,
  );

describe('TerminalHome', () => {
  it('renders heading', () => {
    renderHome();
    expect(screen.getByText('Vyberte operaci')).toBeInTheDocument();
  });

  it('renders an active tile for box checking', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-box-check');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/terminal/box-check');
  });

  it('renders an active tile for box filling', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-box-fill');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/terminal/box-fill');
  });

  it('renders tile for transport-box receiving', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-receive');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/terminal/receive');
  });

  it('renders tile for stocktaking', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-stocktake');
    expect(tile).toHaveAttribute('href', '/terminal/stocktake');
  });

  it('renders tile for lot identification', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-lot-identification');
    expect(tile).toHaveAttribute('href', '/terminal/lot-identification');
  });

  it('shows coming-soon label only on the stub tiles', () => {
    renderHome();
    const labels = screen.getAllByText('Brzy k dispozici');
    expect(labels).toHaveLength(2);
  });
});
