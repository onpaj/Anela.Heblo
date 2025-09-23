/**
 * Unit tests for ChangelogEntry component
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React from 'react';
import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import ChangelogEntry from '../ChangelogEntry';
import { ChangelogEntry as ChangelogEntryType } from '../../types';

const mockEntry: ChangelogEntryType = {
  type: 'funkce',
  title: 'Test feature',
  description: 'Test feature description',
  source: 'commit',
  hash: 'abc123',
};

const mockIssueEntry: ChangelogEntryType = {
  type: 'oprava',
  title: 'Test bug fix',
  description: 'Test bug fix description',
  source: 'github-issue',
  id: '#123',
};

describe('ChangelogEntry component', () => {
  it('renders basic entry correctly', () => {
    render(<ChangelogEntry entry={mockEntry} />);

    expect(screen.getByText('Test feature')).toBeInTheDocument();
    expect(screen.getByText('Test feature description')).toBeInTheDocument();
    expect(screen.getByText('funkce')).toBeInTheDocument();
  });

  it('renders compact mode correctly', () => {
    render(<ChangelogEntry entry={mockEntry} compact={true} />);

    expect(screen.getByText('Test feature')).toBeInTheDocument();
    expect(screen.queryByText('Test feature description')).not.toBeInTheDocument();
  });

  it('shows source information when enabled', () => {
    render(<ChangelogEntry entry={mockEntry} showSource={true} />);

    expect(screen.getByText('abc123')).toBeInTheDocument();
  });

  it('shows GitHub issue source correctly', () => {
    render(<ChangelogEntry entry={mockIssueEntry} showSource={true} />);

    expect(screen.getByText('#123')).toBeInTheDocument();
  });

  it('handles entry without description gracefully', () => {
    const entryWithoutDesc = {
      ...mockEntry,
      description: mockEntry.title,
    };

    render(<ChangelogEntry entry={entryWithoutDesc} />);

    expect(screen.getByText('Test feature')).toBeInTheDocument();
    // Description should not be shown if it's the same as title
    expect(screen.getAllByText('Test feature')).toHaveLength(1);
  });

  it('applies correct color classes for different types', () => {
    const { rerender, container } = render(<ChangelogEntry entry={mockEntry} />);

    // Test funkce (function) type
    expect(container.querySelector('.border-blue-200')).toBeInTheDocument();

    // Test oprava (fix) type
    rerender(<ChangelogEntry entry={mockIssueEntry} />);
    expect(container.querySelector('.border-green-200')).toBeInTheDocument();
  });

  it('renders all change types correctly', () => {
    const changeTypes: ChangelogEntryType['type'][] = [
      'funkce',
      'oprava',
      'vylepšení',
      'výkon',
      'bezpečnost',
      'refaktoring',
      'dokumentace',
      'údržba',
    ];

    changeTypes.forEach((type) => {
      const entry = { ...mockEntry, type };
      render(<ChangelogEntry entry={entry} />);
      expect(screen.getByText(type)).toBeInTheDocument();
    });
  });

  it('handles missing hash gracefully in compact mode with source', () => {
    const entryWithoutHash = {
      ...mockEntry,
      hash: undefined,
    };

    render(
      <ChangelogEntry 
        entry={entryWithoutHash} 
        compact={true} 
        showSource={true} 
      />
    );

    expect(screen.getByText('Test feature')).toBeInTheDocument();
    // Should not crash when hash is undefined
  });

  it('truncates long titles in compact mode', () => {
    const longTitleEntry = {
      ...mockEntry,
      title: 'This is a very long title that should be truncated in compact mode',
    };

    const { container } = render(
      <ChangelogEntry entry={longTitleEntry} compact={true} />
    );

    const titleElement = screen.getByText(longTitleEntry.title);
    expect(titleElement).toHaveClass('truncate');
  });
});