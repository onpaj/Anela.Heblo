# Localization Architecture

This document defines the unified localization approach for the Anela Heblo application.

## Overview

The application uses a centralized localization system based on **i18next** with all translations stored in a single TypeScript file for maintainability and type safety.

## Architecture

### Frontend Localization System

**Framework**: i18next + react-i18next  
**Primary Language**: Czech (cs)  
**Translation File**: `frontend/src/i18n.ts`  
**Error Handling**: Centralized via `errorHandler.ts`

### Translation Structure

All translations are organized in a hierarchical structure within `i18n.ts`:

```typescript
const resources = {
  cs: {
    translation: {
      navigation: { /* Navigation items */ },
      common: { /* Common UI elements */ },
      manufacture: { /* Manufacturing-specific terms */ },
      errors: {
        // Error translations using ErrorCode enum names
        ValidationError: "Chyba validace",
        CannotScheduleInPast: "Nelze naplánovat výrobu do minulosti",
        // ... other error codes
      }
    }
  }
}
```

## Error Localization Pattern

### Backend Error Codes
1. **Backend** defines error codes as enum: `ErrorCodes.CannotScheduleInPast = 1213`
2. **API Response** sends numeric code: `{ success: false, errorCode: 1213 }`

### Frontend Error Translation
1. **Error Handler** receives numeric error code
2. **Converts** to enum name: `ErrorCodes[1213] → "CannotScheduleInPast"`
3. **Looks up translation**: `i18n.t("errors.CannotScheduleInPast")`
4. **Returns** localized message: "Nelze naplánovat výrobu do minulosti"

### Implementation

```typescript
// errorHandler.ts
export function getErrorMessage(errorCode: ErrorCodes, params?: Record<string, string>): string {
  const enumName = ErrorCodes[errorCode];
  if (!enumName) {
    return `Nastala chyba (neznámý kód: ${errorCode})`;
  }

  const messageKey = `errors.${enumName}`;
  const message = i18n.t(messageKey);

  if (message === messageKey || !message) {
    return `Nastala chyba (kód: ${enumName})`;
  }

  return formatMessage(message, params);
}
```

## Rules and Standards

### 1. Single Source of Truth
- ✅ All translations in `frontend/src/i18n.ts`
- ❌ No separate JSON files for translations
- ❌ No inline strings in components

### 2. Error Code Mapping
- ✅ Use ErrorCode enum names as translation keys
- ✅ Format: `errors.{EnumName}` (e.g., `errors.CannotScheduleInPast`)
- ❌ Never use numeric keys in translation files
- ❌ Never hardcode error messages in components

### 3. Parameter Substitution
```typescript
// i18n.ts
"ProductNotFound": "Produkt s kódem {{productCode}} nebyl nalezen"

// Usage
getErrorMessage(ErrorCodes.ProductNotFound, { productCode: "ABC123" })
// Result: "Produkt s kódem ABC123 nebyl nalezen"
```

### 4. Fallback Strategy
1. **Primary**: Look up enum name in translations
2. **Fallback**: Show generic message with enum name
3. **Never**: Show numeric error codes to users

## Quality Assurance

### LocalizationCoverageTests
Automated test ensures all backend ErrorCodes have corresponding frontend translations:

```csharp
[Fact]
public void FrontendI18n_ShouldHaveTranslationsForAllErrorCodes()
{
    // Verifies that every ErrorCodes enum value has a translation
    // in frontend/src/i18n.ts
}
```

### Test Location
- **File**: `backend/test/Anela.Heblo.Tests/LocalizationCoverageTests.cs`
- **Purpose**: Prevent missing translations
- **Run**: `dotnet test --filter "LocalizationCoverageTests"`

## Development Workflow

### Adding New Error Codes
1. **Backend**: Add enum value to `ErrorCodes`
2. **Frontend**: Add translation to `i18n.ts` errors section
3. **Test**: Run `LocalizationCoverageTests` to verify coverage
4. **Format**: Use enum name as key: `{EnumName}: "Czech translation"`

### Adding General Translations
1. **Location**: Add to appropriate section in `i18n.ts`
2. **Structure**: Follow hierarchical organization
3. **Usage**: Access via `t('section.key')` in components

## Migration from Legacy Approaches

### Deprecated Patterns
- ❌ Numeric translation keys (`"1213": "..."`)
- ❌ Separate JSON translation files
- ❌ Hardcoded error messages in components
- ❌ Mixed numeric and string keys

### Migration Steps
1. Move all translations to `i18n.ts`
2. Convert numeric keys to enum names
3. Update error handlers to use enum names
4. Remove duplicate translation files
5. Run tests to verify coverage

## Examples

### Error Translation Example
```typescript
// Backend ErrorCode
CannotScheduleInPast = 1213

// i18n.ts translation
errors: {
  CannotScheduleInPast: "Nelze naplánovat výrobu do minulosti"
}

// Usage in error handler
const message = getErrorMessage(ErrorCodes.CannotScheduleInPast);
// Returns: "Nelze naplánovat výrobu do minulosti"
```

### Component Translation Example
```typescript
// i18n.ts
manufacture: {
  states: {
    Draft: "Návrh",
    Planned: "Naplánováno"
  }
}

// Component usage
const { t } = useTranslation();
const stateLabel = t('manufacture.states.Draft'); // "Návrh"
```

## Benefits

1. **Type Safety**: TypeScript provides compile-time checking
2. **Centralized**: All translations in one place
3. **Maintainable**: Easy to find and update translations
4. **Testable**: Automated coverage verification
5. **Consistent**: Single pattern across entire application
6. **Scalable**: Easy to add new languages or translations

## Future Considerations

- **Multi-language support**: Framework ready for additional languages
- **Translation management**: Consider tools for non-technical translators
- **Dynamic loading**: Optimize bundle size with lazy-loaded translations
- **Pluralization**: Handle Czech plural forms when needed