import React from 'react';
import { CatalogItemDto } from '../../../api/hooks/useCatalog';
import { JournalEntryDto } from '../../../api/generated/api-client';
import JournalEntryModal from '../../JournalEntryModal';
import ManufactureDifficultyModal from '../../ManufactureDifficultyModal';

interface CatalogDetailModalsProps {
  item: CatalogItemDto;
  showJournalModal: boolean;
  onCloseJournalModal: () => void;
  selectedJournalEntry?: JournalEntryDto;
  showManufactureDifficultyModal: boolean;
  onCloseManufactureDifficultyModal: () => void;
  refetchCatalogDetail: () => void;
}

const CatalogDetailModals: React.FC<CatalogDetailModalsProps> = ({
  item,
  showJournalModal,
  onCloseJournalModal,
  selectedJournalEntry,
  showManufactureDifficultyModal,
  onCloseManufactureDifficultyModal,
  refetchCatalogDetail
}) => {
  return (
    <>
      {/* Journal Entry Modal */}
      <JournalEntryModal 
        isOpen={showJournalModal}
        onClose={onCloseJournalModal}
        entry={selectedJournalEntry || {
          associatedProducts: [item.productCode]
        } as any}
        isEdit={!!selectedJournalEntry}
      />
      
      {/* Manufacture Difficulty Modal */}
      <ManufactureDifficultyModal
        isOpen={showManufactureDifficultyModal}
        onClose={() => {
          onCloseManufactureDifficultyModal();
          refetchCatalogDetail();
        }}
        productCode={item.productCode || ''}
        productName={item.productName || ''}
        currentDifficulty={item.manufactureDifficulty}
      />
    </>
  );
};

export default CatalogDetailModals;