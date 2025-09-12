import { parseLocalDate, formatLocalDate, toDateOnlyString, fromDateOnlyString } from '../dateUtils';

describe('dateUtils timezone handling', () => {
  // Store original timezone to restore after tests
  const originalTZ = process.env.TZ;

  afterEach(() => {
    // Restore original timezone
    if (originalTZ) {
      process.env.TZ = originalTZ;
    } else {
      delete process.env.TZ;
    }
  });

  describe('parseLocalDate', () => {
    it('should parse date string as local date regardless of timezone', () => {
      // Test in different timezones
      const testCases = ['UTC', 'Europe/Prague', 'America/New_York', 'Asia/Tokyo'];
      
      testCases.forEach(timezone => {
        process.env.TZ = timezone;
        
        const date = parseLocalDate('2024-01-15');
        
        expect(date.getFullYear()).toBe(2024);
        expect(date.getMonth()).toBe(0); // January is 0
        expect(date.getDate()).toBe(15);
        expect(date.getHours()).toBe(0);
        expect(date.getMinutes()).toBe(0);
        expect(date.getSeconds()).toBe(0);
      });
    });

    it('should handle different date formats consistently', () => {
      const testDates = [
        '2024-01-01',
        '2024-12-31', 
        '2024-06-15'
      ];

      testDates.forEach(dateStr => {
        const date = parseLocalDate(dateStr);
        const [year, month, day] = dateStr.split('-').map(Number);
        
        expect(date.getFullYear()).toBe(year);
        expect(date.getMonth()).toBe(month - 1); // Month is 0-indexed
        expect(date.getDate()).toBe(day);
      });
    });
  });

  describe('formatLocalDate', () => {
    it('should format date consistently regardless of timezone', () => {
      const testCases = ['UTC', 'Europe/Prague', 'America/New_York', 'Asia/Tokyo'];
      
      testCases.forEach(timezone => {
        process.env.TZ = timezone;
        
        const date = new Date(2024, 0, 15); // January 15, 2024 in local time
        const formatted = formatLocalDate(date);
        
        expect(formatted).toBe('2024-01-15');
      });
    });

    it('should handle edge cases correctly', () => {
      const testDates = [
        { date: new Date(2024, 0, 1), expected: '2024-01-01' }, // New Year
        { date: new Date(2024, 11, 31), expected: '2024-12-31' }, // New Year's Eve
        { date: new Date(2024, 1, 29), expected: '2024-02-29' }, // Leap year
      ];

      testDates.forEach(({ date, expected }) => {
        expect(formatLocalDate(date)).toBe(expected);
      });
    });
  });

  describe('round-trip conversion', () => {
    it('should maintain date consistency through parse -> format cycle', () => {
      const testDates = [
        '2024-01-01',
        '2024-06-15', 
        '2024-12-31'
      ];
      
      const timezones = ['UTC', 'Europe/Prague', 'America/New_York', 'Asia/Tokyo'];
      
      timezones.forEach(timezone => {
        process.env.TZ = timezone;
        
        testDates.forEach(originalDate => {
          const parsed = parseLocalDate(originalDate);
          const formatted = formatLocalDate(parsed);
          
          expect(formatted).toBe(originalDate);
        });
      });
    });
  });

  describe('toDateOnlyString and fromDateOnlyString', () => {
    it('should handle null/undefined values', () => {
      expect(toDateOnlyString(null)).toBe(null);
      expect(toDateOnlyString(undefined)).toBe(null);
      expect(fromDateOnlyString(null)).toBe(null);
      expect(fromDateOnlyString(undefined)).toBe(null);
    });

    it('should convert Date to ISO date string', () => {
      const date = new Date(2024, 0, 15); // January 15, 2024
      const result = toDateOnlyString(date);
      expect(result).toBe('2024-01-15');
    });

    it('should convert ISO date string to Date', () => {
      const result = fromDateOnlyString('2024-01-15');
      expect(result).toBeInstanceOf(Date);
      expect(result?.getFullYear()).toBe(2024);
      expect(result?.getMonth()).toBe(0); // January is 0
      expect(result?.getDate()).toBe(15);
    });

    it('should maintain consistency through round-trip conversion', () => {
      const timezones = ['UTC', 'Europe/Prague', 'America/New_York', 'Asia/Tokyo'];
      
      timezones.forEach(timezone => {
        process.env.TZ = timezone;
        
        const originalDate = new Date(2024, 0, 15);
        const asString = toDateOnlyString(originalDate);
        const backToDate = fromDateOnlyString(asString!);
        
        expect(backToDate?.getFullYear()).toBe(originalDate.getFullYear());
        expect(backToDate?.getMonth()).toBe(originalDate.getMonth());
        expect(backToDate?.getDate()).toBe(originalDate.getDate());
      });
    });
  });

  describe('problematic timezone scenarios', () => {
    it('should not shift dates when crossing DST boundaries', () => {
      // Test around DST transition dates in Europe/Prague
      process.env.TZ = 'Europe/Prague';
      
      const dstTestDates = [
        '2024-03-30', // Day before DST starts in Europe
        '2024-03-31', // DST starts in Europe
        '2024-10-26', // Day before DST ends in Europe
        '2024-10-27'  // DST ends in Europe
      ];
      
      dstTestDates.forEach(dateStr => {
        const parsed = parseLocalDate(dateStr);
        const formatted = formatLocalDate(parsed);
        
        expect(formatted).toBe(dateStr);
      });
    });

    it('should handle New Year boundary correctly', () => {
      const timezones = ['UTC', 'Europe/Prague', 'America/New_York', 'Asia/Tokyo'];
      
      timezones.forEach(timezone => {
        process.env.TZ = timezone;
        
        // Test New Year's Eve and New Year's Day
        const newYearEve = parseLocalDate('2023-12-31');
        const newYearDay = parseLocalDate('2024-01-01');
        
        expect(formatLocalDate(newYearEve)).toBe('2023-12-31');
        expect(formatLocalDate(newYearDay)).toBe('2024-01-01');
      });
    });
  });
});