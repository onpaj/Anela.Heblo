import { render, screen } from '@testing-library/react';
import FolderStatusBanner from '../FolderStatusBanner';

describe('FolderStatusBanner', () => {
  it('renders nothing when status is Found', () => {
    const { container } = render(
      <FolderStatusBanner status="Found" expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('shows not-found message with prefix and basePath', () => {
    render(
      <FolderStatusBanner status="NotFound" expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(screen.getByText(/MAT001__/)).toBeInTheDocument();
    expect(screen.getByText(/\/Materials\/Documents/)).toBeInTheDocument();
  });

  it('shows multiple-matches warning', () => {
    render(
      <FolderStatusBanner status="MultipleMatches" expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(screen.getByText(/více složek/i)).toBeInTheDocument();
  });
});
