import React from "react";
import { TransportBoxModalsProps } from "./TransportBoxTypes";
import AddItemToBoxModal from "../../pages/AddItemToBoxModal";
import LocationSelectionModal from "../../pages/LocationSelectionModal";

const TransportBoxModals: React.FC<TransportBoxModalsProps> = ({
  transportBox,
  isAddItemModalOpen,
  setIsAddItemModalOpen,
  isLocationSelectionModalOpen,
  setIsLocationSelectionModalOpen,
  handleAddItemSuccess,
  handleLocationSelectionSuccess,
}) => {
  return (
    <>
      <AddItemToBoxModal
        isOpen={isAddItemModalOpen}
        onClose={() => setIsAddItemModalOpen(false)}
        boxId={transportBox.id || null}
        onSuccess={handleAddItemSuccess}
      />

      <LocationSelectionModal
        isOpen={isLocationSelectionModalOpen}
        onClose={() => setIsLocationSelectionModalOpen(false)}
        boxId={transportBox.id || null}
        onSuccess={handleLocationSelectionSuccess}
      />
    </>
  );
};

export default TransportBoxModals;
