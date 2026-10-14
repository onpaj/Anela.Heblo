import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import KbQuestionTagFilters from '../KbQuestionTagFilters';

const problemOptions = [{ tag: 'akné', label: 'akné', count: 5 }, { tag: 'růžovka', label: 'růžovka', count: 2 }];
const productOptions = [{ tag: 'hořčíkové máslo', label: 'hořčíkové máslo', count: 1 }];

const setupFilters = (onChange = jest.fn(), props: Partial<React.ComponentProps<typeof KbQuestionTagFilters>> = {}) => {
  render(<KbQuestionTagFilters problemOptions={problemOptions} productOptions={productOptions} onChange={onChange} {...props} />);
  return onChange;
};

test('shows options with counts', () => {
  setupFilters();
  expect(screen.getByRole('option', { name: 'akné (5)' })).toBeInTheDocument();
  expect(screen.getByRole('option', { name: 'hořčíkové máslo (1)' })).toBeInTheDocument();
});

test('selecting a problem emits problemTag', () => {
  const onChange = setupFilters();
  fireEvent.change(screen.getByLabelText('Problém:'), { target: { value: 'růžovka' } });
  expect(onChange).toHaveBeenCalledWith({ problemTag: 'růžovka' });
});

test('choosing "Vše" clears the tag', () => {
  const onChange = setupFilters(jest.fn(), { problemTag: 'akné' });
  fireEvent.change(screen.getByLabelText('Problém:'), { target: { value: '' } });
  expect(onChange).toHaveBeenCalledWith({ problemTag: undefined });
});

test('date inputs emit yyyy-MM-dd strings and clear to undefined', () => {
  const onChange = setupFilters(jest.fn(), { dateTo: '2026-10-05' });
  fireEvent.change(screen.getByLabelText('Od:'), { target: { value: '2026-10-01' } });
  expect(onChange).toHaveBeenCalledWith({ dateFrom: '2026-10-01' });
  fireEvent.change(screen.getByLabelText('Do:'), { target: { value: '' } });
  expect(onChange).toHaveBeenCalledWith({ dateTo: undefined });
});

test('keeps a selected tag visible even when it is not in the current range options', () => {
  setupFilters(jest.fn(), { problemTag: 'ekzém' });
  expect(screen.getByLabelText('Problém:')).toHaveValue('ekzém');
});
