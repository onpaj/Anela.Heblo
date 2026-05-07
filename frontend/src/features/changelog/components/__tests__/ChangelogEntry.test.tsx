/**
 * Unit tests for ChangelogEntry component
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import ChangelogEntry from '../ChangelogEntry';
import { ChangelogEntry as ChangelogEntryType } from '../../types';

const baseEntry: ChangelogEntryType = {
  type: 'feature',
  title: 'some title',
  description: 'some description',
  source: 'commit',
  hash: 'abc1234',
};

describe('ChangelogEntry component — showModule logic', () => {
  it('does not render module badge when entry.module is undefined', () => {
    render(<ChangelogEntry entry={baseEntry} />);

    expect(screen.queryByText(/Sklad/)).toBeNull();
  });

  it('does not render module badge when entry.module is "Ostatní"', () => {
    const entry: ChangelogEntryType = { ...baseEntry, module: 'Ostatní' };

    render(<ChangelogEntry entry={entry} />);

    expect(screen.queryByText('Ostatní')).toBeNull();
  });

  it('renders module badge when entry.module is a real string', () => {
    const entry: ChangelogEntryType = { ...baseEntry, module: 'Sklad' };

    render(<ChangelogEntry entry={entry} />);

    expect(screen.getByText('Sklad')).toBeInTheDocument();
  });
});

describe('ChangelogEntry component — full view', () => {
  it('renders module badge after type badge when module is present', () => {
    const entry: ChangelogEntryType = { ...baseEntry, module: 'Sklad' };

    render(<ChangelogEntry entry={entry} />);

    const moduleBadge = screen.getByText('Sklad');
    expect(moduleBadge).toBeInTheDocument();
    expect(moduleBadge.className).toContain('bg-slate-100');
    expect(moduleBadge.className).toContain('text-slate-700');
  });

  it('does not render module badge in full view when module is absent', () => {
    render(<ChangelogEntry entry={baseEntry} />);

    expect(screen.queryByText('Sklad')).not.toBeInTheDocument();
  });
});

describe('ChangelogEntry component — compact view', () => {
  it('prefixes title with module name when module is present', () => {
    const entry: ChangelogEntryType = { ...baseEntry, module: 'Sklad' };

    render(<ChangelogEntry entry={entry} compact={true} />);

    expect(screen.getByText(/Sklad:/)).toBeInTheDocument();
    expect(screen.getByText('some title')).toBeInTheDocument();
  });

  it('does not render module prefix in compact view when module is absent', () => {
    render(<ChangelogEntry entry={baseEntry} compact={true} />);

    expect(screen.queryByText(/^Sklad/)).toBeNull();
    expect(screen.getByText('some title')).toBeInTheDocument();
  });

  it('does not render module prefix in compact view when module is "Ostatní"', () => {
    const entry: ChangelogEntryType = { ...baseEntry, module: 'Ostatní' };

    render(<ChangelogEntry entry={entry} compact={true} />);

    expect(screen.queryByText(/Ostatní/)).toBeNull();
    expect(screen.getByText('some title')).toBeInTheDocument();
  });
});
