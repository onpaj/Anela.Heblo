import React, { createContext, useContext, useState, ReactNode } from "react";

interface PurchasePlanningListItem {
  productCode: string;
  productName: string;
  supplier: string;
  supplierCode?: string;
  addedAt: Date;
}

interface PurchasePlanningListContextType {
  items: PurchasePlanningListItem[];
  addItem: (material: { code: string; name: string; supplier: string; supplierCode?: string }) => void;
  removeItem: (productCode: string) => void;
  clearList: () => void;
  hasItems: boolean;
}

const PurchasePlanningListContext = createContext<PurchasePlanningListContextType | undefined>(undefined);

export const usePurchasePlanningList = () => {
  const context = useContext(PurchasePlanningListContext);
  if (context === undefined) {
    throw new Error("usePurchasePlanningList must be used within a PurchasePlanningListProvider");
  }
  return context;
};

interface PurchasePlanningListProviderProps {
  children: ReactNode;
}

const PURCHASE_PLANNING_LIST_MAX_ITEMS = 20;

export const PurchasePlanningListProvider: React.FC<PurchasePlanningListProviderProps> = ({
  children,
}) => {
  const [items, setItems] = useState<PurchasePlanningListItem[]>([]);

  const addItem = (material: { code: string; name: string; supplier: string; supplierCode?: string }) => {
    setItems((prev) => {
      // Check if material already exists
      const exists = prev.some(item => item.productCode === material.code);
      if (exists) {
        return prev; // No-op if already exists
      }

      // Check if we're at max capacity
      if (prev.length >= PURCHASE_PLANNING_LIST_MAX_ITEMS) {
        return prev; // No-op if at max capacity
      }

      // Add new item
      const newItem: PurchasePlanningListItem = {
        productCode: material.code,
        productName: material.name,
        supplier: material.supplier,
        supplierCode: material.supplierCode,
        addedAt: new Date(),
      };

      return [...prev, newItem];
    });
  };

  const removeItem = (productCode: string) => {
    setItems((prev) => prev.filter(item => item.productCode !== productCode));
  };

  const clearList = () => {
    setItems([]);
  };

  const hasItems = items.length > 0;

  return (
    <PurchasePlanningListContext.Provider
      value={{
        items,
        addItem,
        removeItem,
        clearList,
        hasItems,
      }}
    >
      {children}
    </PurchasePlanningListContext.Provider>
  );
};

export type { PurchasePlanningListItem };