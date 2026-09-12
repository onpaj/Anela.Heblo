import React from 'react';
import { render, screen } from '@testing-library/react';
import RunDqtButton from '../RunDqtButton';
import { useRunDqt } from '../../../api/hooks/useDataQuality';

jest.mock('../../../api/hooks/useDataQuality', () => ({
  useRunDqt: jest.fn(),
}));

const mockUseRunDqt = useRunDqt as jest.Mock;

beforeEach(() => {
  mockUseRunDqt.mockReturnValue({ mutate: jest.fn(), isPending: false });
});

test('offers "Kontrola cen" as a selectable test type', () => {
  // Act
  render(<RunDqtButton />);

  // Assert
  expect(
    screen.getByRole('option', { name: 'Kontrola cen', exact: true }),
  ).toBeInTheDocument();
});
