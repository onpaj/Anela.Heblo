import { JournalEntryDto } from '../../../../api/generated/api-client';
import { 
  CatalogSalesRecordDto, 
  CatalogConsumedRecordDto, 
  CatalogPurchaseRecordDto, 
  CatalogManufactureRecordDto 
} from '../../../../api/hooks/useCatalog';

// Generate last 13 months labels
export const generateMonthLabels = (): string[] => {
  const months = [];
  const now = new Date();
  
  for (let i = 12; i >= 0; i--) {
    const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
    months.push(date.toLocaleDateString('cs-CZ', { month: 'short', year: 'numeric' }));
  }
  
  return months;
};

// Helper function to get journal entries for a specific month
export const getJournalEntriesForMonth = (monthIndex: number, journalEntries: JournalEntryDto[]): JournalEntryDto[] => {
  if (!journalEntries || journalEntries.length === 0) return [];
  
  const now = new Date();
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1; // JavaScript months are 0-based, convert to 1-based
  
  // Calculate the target year and month for the given monthIndex
  const monthsBack = 12 - monthIndex; // 12 months back to current month
  let targetYear = currentYear;
  let targetMonth = currentMonth - monthsBack;
  
  // Handle year transitions
  if (targetMonth <= 0) {
    targetYear--;
    targetMonth += 12;
  }
  
  // Filter journal entries for this specific month and year
  return journalEntries.filter(entry => {
    if (!entry.entryDate) return false;
    
    const entryDate = new Date(entry.entryDate);
    const entryYear = entryDate.getFullYear();
    const entryMonth = entryDate.getMonth() + 1; // Convert to 1-based
    
    return entryYear === targetYear && entryMonth === targetMonth;
  });
};

// Map data to monthly array based on year/month
export const mapDataToMonthlyArray = (
  data: CatalogSalesRecordDto[] | CatalogConsumedRecordDto[] | CatalogPurchaseRecordDto[] | CatalogManufactureRecordDto[], 
  valueKey: 'amountTotal' | 'amount'
): number[] => {
  const monthlyData = new Array(13).fill(0);
  const now = new Date();
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1; // JavaScript months are 0-based, convert to 1-based
  
  // Create a map for quick lookup of data by year-month key
  const dataMap = new Map<string, number>();
  data.forEach(record => {
    if (record.year && record.month) {
      const key = `${record.year}-${record.month}`;
      const value = (record as any)[valueKey] || 0;
      dataMap.set(key, value);
    }
  });
  
  // Fill the array with data for the last 13 months
  for (let i = 0; i < 13; i++) {
    const monthsBack = 12 - i; // 12 months back to current month
    let adjustedYear = currentYear;
    let adjustedMonth = currentMonth - monthsBack;
    
    // Handle year transitions
    if (adjustedMonth <= 0) {
      adjustedYear--;
      adjustedMonth += 12;
    }
    
    const key = `${adjustedYear}-${adjustedMonth}`;
    const value = dataMap.get(key) || 0;
    monthlyData[i] = value;
  }
  return monthlyData;
};

// Generate point styling arrays based on journal entries
export const generatePointStyling = (
  dataLength: number, 
  journalEntries: JournalEntryDto[],
  defaultColor: string,
  journalColor: string = '#F97316'
) => {
  const pointBackgroundColors = [];
  const pointRadiuses = [];
  
  for (let i = 0; i < dataLength; i++) {
    const monthEntries = getJournalEntriesForMonth(i, journalEntries);
    const hasJournalEntries = monthEntries.length > 0;
    
    pointBackgroundColors.push(hasJournalEntries ? journalColor : defaultColor);
    pointRadiuses.push(hasJournalEntries ? 6 : 3);
  }
  
  return {
    pointBackgroundColors,
    pointRadiuses,
    pointHoverRadiuses: pointRadiuses.map(r => r + 2)
  };
};

// Generate tooltip callback for journal entries
export const generateTooltipCallback = (journalEntries: JournalEntryDto[]) => ({
  afterBody: (context: any[]) => {
    if (context.length === 0) return [];
    
    // Get the month index from the first context item
    const monthIndex = context[0].dataIndex;
    const monthEntries = getJournalEntriesForMonth(monthIndex, journalEntries);
    
    if (monthEntries.length === 0) return [];
    
    const journalLines = ['', 'Záznamy deníku:'];
    monthEntries.forEach(entry => {
      const date = entry.entryDate ? new Date(entry.entryDate).toLocaleDateString('cs-CZ', { day: '2-digit', month: '2-digit' }) : '';
      const title = entry.title || 'Bez názvu';
      journalLines.push(`• ${date}: ${title}`);
    });
    
    return journalLines;
  }
});