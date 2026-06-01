import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LotIdentificationHome from '../LotIdentificationHome';

const renderHome = () =>
  render(
    <MemoryRouter>
      <LotIdentificationHome />
    </MemoryRouter>
  );

test('shows two mode tiles', () => {
  renderHome();
  expect(screen.getByText(/Příjem podle objednávky/i)).toBeInTheDocument();
  expect(screen.getByText(/Volný příjem/i)).toBeInTheDocument();
});

test('PO mode tile links to /terminal/lot-identification/po', () => {
  renderHome();
  const link = screen.getByRole('link', { name: /Příjem podle objednávky/i });
  expect(link).toHaveAttribute('href', '/terminal/lot-identification/po');
});

test('freeform mode tile links to /terminal/lot-identification/freeform', () => {
  renderHome();
  const link = screen.getByRole('link', { name: /Volný příjem/i });
  expect(link).toHaveAttribute('href', '/terminal/lot-identification/freeform');
});
