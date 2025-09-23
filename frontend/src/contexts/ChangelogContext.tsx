/**
 * Changelog context for managing modal state globally
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import React, { createContext, useContext, useState, ReactNode } from 'react';

interface ChangelogContextType {
  isModalOpen: boolean;
  openModal: () => void;
  closeModal: () => void;
}

const ChangelogContext = createContext<ChangelogContextType | undefined>(undefined);

interface ChangelogProviderProps {
  children: ReactNode;
}

export const ChangelogProvider: React.FC<ChangelogProviderProps> = ({ children }) => {
  const [isModalOpen, setIsModalOpen] = useState(false);

  const openModal = () => setIsModalOpen(true);
  const closeModal = () => setIsModalOpen(false);

  return (
    <ChangelogContext.Provider value={{ isModalOpen, openModal, closeModal }}>
      {children}
    </ChangelogContext.Provider>
  );
};

export const useChangelogContext = (): ChangelogContextType => {
  const context = useContext(ChangelogContext);
  if (!context) {
    throw new Error('useChangelogContext must be used within a ChangelogProvider');
  }
  return context;
};