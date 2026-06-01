import React, { createContext, useContext, useState, ReactNode } from "react";

interface PlanningListItem {
  productCode: string;
  productName: string;
  addedAt: Date;
}

interface PlanningListContextType {
  items: PlanningListItem[];
  addItem: (product: { code: string; name: string }) => void;
  removeItem: (productCode: string) => void;
  clearList: () => void;
  hasItems: boolean;
}

const PlanningListContext = createContext<PlanningListContextType | undefined>(undefined);

export const usePlanningList = () => {
  const context = useContext(PlanningListContext);
  if (context === undefined) {
    throw new Error("usePlanningList must be used within a PlanningListProvider");
  }
  return context;
};

interface PlanningListProviderProps {
  children: ReactNode;
}

const PLANNING_LIST_MAX_ITEMS = 20;

export const PlanningListProvider: React.FC<PlanningListProviderProps> = ({
  children,
}) => {
  const [items, setItems] = useState<PlanningListItem[]>([]);

  const addItem = (product: { code: string; name: string }) => {
    setItems((prev) => {
      // Check if product already exists
      const exists = prev.some(item => item.productCode === product.code);
      if (exists) {
        return prev; // No-op if already exists
      }

      // Check if we're at max capacity
      if (prev.length >= PLANNING_LIST_MAX_ITEMS) {
        return prev; // No-op if at max capacity
      }

      // Add new item
      const newItem: PlanningListItem = {
        productCode: product.code,
        productName: product.name,
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
    <PlanningListContext.Provider
      value={{
        items,
        addItem,
        removeItem,
        clearList,
        hasItems,
      }}
    >
      {children}
    </PlanningListContext.Provider>
  );
};

export type { PlanningListItem };