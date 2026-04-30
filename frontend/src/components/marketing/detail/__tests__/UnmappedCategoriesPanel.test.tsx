import React from 'react';
import { render, screen } from '@testing-library/react';
import { UnmappedCategoriesPanel } from '../UnmappedCategoriesPanel';

describe('UnmappedCategoriesPanel', () => {
  it('renders heading text', () => {
    render(<UnmappedCategoriesPanel categories={['X']} />);
    expect(screen.getByText(/Nemapované kategorie z Outlooku/)).toBeInTheDocument();
  });

  it('renders subtext mentioning appsettings.json', () => {
    render(<UnmappedCategoriesPanel categories={['X']} />);
    expect(screen.getByText(/appsettings\.json/)).toBeInTheDocument();
  });

  it('renders subtext mentioning General as default category', () => {
    render(<UnmappedCategoriesPanel categories={['X']} />);
    expect(screen.getByText(/výchozí kategorie \(General\)/)).toBeInTheDocument();
  });

  it('renders one pill per category', () => {
    render(<UnmappedCategoriesPanel categories={['A', 'B', 'C']} />);
    expect(screen.getAllByTestId('category-pill')).toHaveLength(3);
  });

  it('pills display exact category name with special chars', () => {
    render(<UnmappedCategoriesPanel categories={['PR – léto']} />);
    expect(screen.getByText('PR – léto')).toBeInTheDocument();
  });
});
