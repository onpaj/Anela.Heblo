import React from 'react';
import { render, screen } from '@testing-library/react';
import { SubjectHeader } from '../SubjectHeader';

it('renders the empty prompt when no code', () => {
  render(<SubjectHeader emptyPrompt="Naskenujte box" />);
  expect(screen.getByText('Naskenujte box')).toBeInTheDocument();
});

it('renders code + state badge + facts when given a subject', () => {
  render(<SubjectHeader code="B001" state="Opened" facts={<span>3 položky</span>} />);
  expect(screen.getByText('B001')).toBeInTheDocument();
  expect(screen.getByText('Otevřený')).toBeInTheDocument(); // stateLabels[Opened]
  expect(screen.getByText('3 položky')).toBeInTheDocument();
});
