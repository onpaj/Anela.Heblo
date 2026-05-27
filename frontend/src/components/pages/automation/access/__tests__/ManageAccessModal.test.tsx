import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ManageAccessModal } from '../ManageAccessModal';
import type { MeetingTranscriptDto, MeetingUserDto } from '../../../../../api/hooks/useMeetingTasks';

import { useUpdateMeetingAccess } from '../../../../../api/hooks/useMeetingTasks';

jest.mock('react-select', () => ({
  __esModule: true,
  default: jest.fn(() => null),
  components: { Option: () => null, SingleValue: () => null },
}));

jest.mock('../../../../../api/hooks/useMeetingTasks', () => ({
  ...jest.requireActual('../../../../../api/hooks/useMeetingTasks'),
  useUpdateMeetingAccess: jest.fn(),
}));

const mockTranscript: Partial<MeetingTranscriptDto> = {
  id: 'test-id',
  subject: 'Test Meeting',
  accessLevel: 'Private',
  accessGrants: [],
};

const mockUsers: MeetingUserDto[] = [
  { email: 'alice@test.com', displayName: 'Alice', aliases: [] },
  { email: 'bob@test.com', displayName: 'Bob', aliases: [] },
];

describe('ManageAccessModal', () => {
  const mockMutate = jest.fn();

  beforeEach(() => {
    (useUpdateMeetingAccess as jest.Mock).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
      error: null,
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('renders nothing when isOpen is false', () => {
    render(
      <ManageAccessModal
        isOpen={false}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );
    expect(screen.queryByText('Spravovat přístup ke schůzce')).not.toBeInTheDocument();
  });

  it('renders modal with access level radios when open', () => {
    render(
      <ManageAccessModal
        isOpen={true}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );
    expect(screen.getByText('Spravovat přístup ke schůzce')).toBeInTheDocument();
    expect(screen.getByLabelText(/Soukromé/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Veřejné/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Omezené/)).toBeInTheDocument();
  });

  it('calls mutate with Public access on save', () => {
    render(
      <ManageAccessModal
        isOpen={true}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );

    fireEvent.click(screen.getByLabelText(/Veřejné/));
    fireEvent.click(screen.getByText('Uložit'));

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({ accessLevel: 'Public', restrictedUserEmails: [] }),
      expect.anything()
    );
  });

  it('disables Save button when Restricted with no users selected', () => {
    render(
      <ManageAccessModal
        isOpen={true}
        onClose={jest.fn()}
        transcript={mockTranscript as MeetingTranscriptDto}
        users={mockUsers}
      />
    );

    fireEvent.click(screen.getByLabelText(/Omezené/));

    expect(screen.getByText('Uložit')).toBeDisabled();
  });
});
