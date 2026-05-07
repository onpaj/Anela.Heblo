import React from 'react';
import { render, screen } from '@testing-library/react';
import { ConditionsTile } from '../ConditionsTile';

const FIXED_TIME = '2026-05-07T10:00:00.000Z';

const liveData = {
  status: 'success',
  data: {
    innerTemperature: 21.5,
    innerHumidity: 55,
    outerTemperature: 14.2,
    outerHumidity: 72,
    recordedAt: FIXED_TIME,
    source: 'Live' as const,
  },
};

describe('ConditionsTile', () => {
  it('renders all four values with correct units when source is Live', () => {
    render(<ConditionsTile data={liveData} />);

    expect(screen.getByText('21.5 °C')).toBeInTheDocument();
    expect(screen.getByText('14.2 °C')).toBeInTheDocument();
    expect(screen.getByText('55 %')).toBeInTheDocument();
    expect(screen.getByText('72 %')).toBeInTheDocument();
  });

  it('shows green Živé badge when source is Live', () => {
    render(<ConditionsTile data={liveData} />);

    const badge = screen.getByText('Živé');
    expect(badge).toBeInTheDocument();
    expect(badge.className).toContain('text-green-700');
  });

  it('renders em-dash placeholders for null readings (Partial)', () => {
    const partialData = {
      status: 'success',
      data: {
        innerTemperature: 20.0,
        innerHumidity: null,
        outerTemperature: null,
        outerHumidity: null,
        recordedAt: FIXED_TIME,
        source: 'Partial' as const,
      },
    };

    render(<ConditionsTile data={partialData} />);

    expect(screen.getByText('20.0 °C')).toBeInTheDocument();
    expect(screen.getAllByText('—')).toHaveLength(3);
  });

  it('shows amber Částečné badge when source is Partial', () => {
    render(<ConditionsTile data={{ ...liveData, data: { ...liveData.data, source: 'Partial' } }} />);

    const badge = screen.getByText('Částečné');
    expect(badge).toBeInTheDocument();
    expect(badge.className).toContain('text-amber-700');
  });

  it('shows red Nedostupné badge and all em-dashes when source is Unavailable', () => {
    const unavailableData = {
      status: 'success',
      data: {
        innerTemperature: null,
        innerHumidity: null,
        outerTemperature: null,
        outerHumidity: null,
        recordedAt: FIXED_TIME,
        source: 'Unavailable' as const,
      },
    };

    render(<ConditionsTile data={unavailableData} />);

    const badge = screen.getByText('Nedostupné');
    expect(badge).toBeInTheDocument();
    expect(badge.className).toContain('text-red-700');
    expect(screen.getAllByText('—')).toHaveLength(4);
  });

  it('renders nothing when data.data is undefined', () => {
    const { container } = render(<ConditionsTile data={{ status: 'success' }} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders all four labels', () => {
    render(<ConditionsTile data={liveData} />);

    expect(screen.getByText('Vnitřní teplota')).toBeInTheDocument();
    expect(screen.getByText('Venkovní teplota')).toBeInTheDocument();
    expect(screen.getByText('Vnitřní vlhkost')).toBeInTheDocument();
    expect(screen.getByText('Venkovní vlhkost')).toBeInTheDocument();
  });
});
