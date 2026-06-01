import React from "react";

interface InventoryStatusCellProps {
  lastStockTaking: Date | null | undefined;
  onClick: () => void;
}

const InventoryStatusCell: React.FC<InventoryStatusCellProps> = ({ 
  lastStockTaking, 
  onClick 
}) => {
  const calculateDaysSinceInventory = (date: Date | null | undefined): number | null => {
    if (!date) return null;
    
    const now = new Date();
    const stockTakingDate = new Date(date);
    const diffTime = Math.abs(now.getTime() - stockTakingDate.getTime());
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
    
    return diffDays;
  };

  const getInventoryColorClass = (daysSince: number | null): string => {
    if (daysSince === null) {
      // Never inventoried - red
      return "bg-red-500 text-white hover:bg-red-600";
    }
    
    if (daysSince < 120) {
      // Fresh - green
      return "bg-green-500 text-white hover:bg-green-600";
    }
    
    if (daysSince <= 250) {
      // Warning - orange
      return "bg-orange-500 text-white hover:bg-orange-600";
    }
    
    // Critical - red
    return "bg-red-500 text-white hover:bg-red-600";
  };

  const daysSinceInventory = calculateDaysSinceInventory(lastStockTaking);
  const colorClass = getInventoryColorClass(daysSinceInventory);

  return (
    <button 
      onClick={onClick}
      className={`px-3 py-1 rounded-full text-sm font-medium cursor-pointer transition-colors duration-200 ${colorClass}`}
      title="Klikněte pro inventarizaci materiálu"
    >
      {daysSinceInventory !== null ? `${daysSinceInventory} d` : 'Nikdy'}
    </button>
  );
};

export default InventoryStatusCell;