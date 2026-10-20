import React from 'react';
import { render, screen } from '@testing-library/react';
import TagChips from '../TagChips';

test('renders one chip per tag', () => {
  render(<TagChips tags={['akné', 'hořčíkové máslo']} />);
  expect(screen.getByText('akné')).toBeInTheDocument();
  expect(screen.getByText('hořčíkové máslo')).toBeInTheDocument();
});

test('renders nothing when there are no tags', () => {
  const { container } = render(<TagChips tags={[]} />);
  expect(container).toBeEmptyDOMElement();
});
