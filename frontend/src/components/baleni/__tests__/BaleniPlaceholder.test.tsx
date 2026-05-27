import React from 'react';
import { render, screen } from '@testing-library/react';
import BaleniPlaceholder from '../BaleniPlaceholder';

describe('BaleniPlaceholder', () => {
  it('renders the passed title', () => {
    render(<BaleniPlaceholder title="Balení" />);
    expect(screen.getByText('Balení')).toBeInTheDocument();
  });

  it('renders "Brzy k dispozici" message', () => {
    render(<BaleniPlaceholder title="Zásilky" />);
    expect(screen.getByText('Brzy k dispozici')).toBeInTheDocument();
  });
});
