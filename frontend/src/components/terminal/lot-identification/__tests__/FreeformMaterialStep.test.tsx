import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import FreeformMaterialStep from '../FreeformMaterialStep';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

beforeEach(() => mockNavigate.mockReset());

test('on scan, navigates to lot-entry step with material code in URL', () => {
  render(<MemoryRouter><FreeformMaterialStep /></MemoryRouter>);
  const input = screen.getByRole('textbox') as HTMLInputElement;
  fireEvent.change(input, { target: { value: 'MAT001' } });
  fireEvent.submit(screen.getByRole('form'));
  expect(mockNavigate).toHaveBeenCalledWith('/terminal/lot-identification/freeform/MAT001/lot');
});
