import React from 'react';
import { render, screen } from '@testing-library/react';
import ConditionsReadingsSection from '../ConditionsReadingsSection';
import {
  ManufactureOrderConditionsReadingDto,
  ManufactureOrderState,
} from '../../../../api/generated/api-client';

const mockReading = (
  stage: ManufactureOrderState,
  overrides: Partial<ManufactureOrderConditionsReadingDto> = {}
): ManufactureOrderConditionsReadingDto => ({
  id: 1,
  stage,
  innerTemperature: 21.5,
  innerHumidity: 55.0,
  outerTemperature: 18.2,
  outerHumidity: 72.3,
  recordedAt: new Date('2026-05-06T10:30:00Z'),
  source: 1, // Live
  ...overrides,
});

describe('ConditionsReadingsSection', () => {
  test('renders section heading', () => {
    render(<ConditionsReadingsSection readings={[]} />);
    expect(screen.getByText(/Podmínky/i)).toBeInTheDocument();
  });

  test('renders em-dashes when no readings provided', () => {
    render(<ConditionsReadingsSection readings={[]} />);
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  test('renders temperature and humidity values when readings present', () => {
    const readings = [mockReading(ManufactureOrderState.SemiProductManufactured)];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText('21.5')).toBeInTheDocument();
    expect(screen.getByText('55.0')).toBeInTheDocument();
    expect(screen.getByText('18.2')).toBeInTheDocument();
    expect(screen.getByText('72.3')).toBeInTheDocument();
  });

  test('renders null cell as em-dash', () => {
    const readings = [mockReading(ManufactureOrderState.SemiProductManufactured, { innerTemperature: null as unknown as undefined })];
    render(<ConditionsReadingsSection readings={readings} />);
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  test('shows HA nedostupný badge when source is Unavailable (3)', () => {
    const readings = [mockReading(ManufactureOrderState.SemiProductManufactured, { source: 3 })];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText(/HA nedostupný/i)).toBeInTheDocument();
  });

  test('shows Částečné badge when source is Partial (2)', () => {
    const readings = [mockReading(ManufactureOrderState.SemiProductManufactured, { source: 2 })];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText(/Částečné/i)).toBeInTheDocument();
  });

  test('shows no badge when source is Live (1)', () => {
    const readings = [mockReading(ManufactureOrderState.SemiProductManufactured, { source: 1 })];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.queryByText(/HA nedostupný/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Částečné/i)).not.toBeInTheDocument();
  });

  test('renders both stages when two readings present', () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured),
      mockReading(ManufactureOrderState.Completed, { id: 2, innerTemperature: 22.0 }),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText('22.0')).toBeInTheDocument();
    expect(screen.getByText('21.5')).toBeInTheDocument();
  });
});
