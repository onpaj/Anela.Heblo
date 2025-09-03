import { generateMonthLabels, mapDataToMonthlyArray } from '../ChartHelpers';

describe('ChartHelpers', () => {
  describe('generateMonthLabels', () => {
    it('generates 13 month labels', () => {
      const labels = generateMonthLabels();
      expect(labels).toHaveLength(13);
      expect(labels[0]).toMatch(/^.+ \d{4}$/); // Format like "září 2024"
      expect(labels[12]).toMatch(/^.+ \d{4}$/);
    });
  });

  describe('mapDataToMonthlyArray', () => {
    it('maps data to monthly array correctly', () => {
      const currentDate = new Date();
      const currentYear = currentDate.getFullYear();
      const currentMonth = currentDate.getMonth() + 1;
      
      const mockData = [
        { year: currentYear, month: currentMonth, amount: 100 },
        { year: currentYear, month: currentMonth - 1 > 0 ? currentMonth - 1 : 12, amount: 50 }
      ];

      const result = mapDataToMonthlyArray(mockData, 'amount');
      
      expect(result).toHaveLength(13);
      expect(result[12]).toBe(100); // Current month should be last in array
      expect(result.some(val => val === 50)).toBe(true); // Previous month data should be present
    });

    it('handles empty data array', () => {
      const result = mapDataToMonthlyArray([], 'amount');
      expect(result).toHaveLength(13);
      expect(result.every(val => val === 0)).toBe(true);
    });
  });
});