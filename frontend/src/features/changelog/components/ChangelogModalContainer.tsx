/**
 * Changelog modal container that uses context
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React from 'react';
import { useChangelogContext } from '../../../contexts/ChangelogContext';
import ChangelogModal from './ChangelogModal';

/**
 * Container component that connects the modal to the context
 */
const ChangelogModalContainer: React.FC = () => {
  const { isModalOpen, closeModal } = useChangelogContext();

  return (
    <ChangelogModal
      isOpen={isModalOpen}
      onClose={closeModal}
    />
  );
};

export default ChangelogModalContainer;