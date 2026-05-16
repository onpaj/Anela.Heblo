# Pattern: Terminal Barcode / Code Input

Every terminal screen that accepts a scanned or manually typed barcode,
box code, EAN, or similar identifier **MUST** use the shared component:

```
frontend/src/components/terminal/ScanInput.tsx
```

**Never** add a raw `<input>` or another ad-hoc text field for code entry.

## Props reference

| Prop | Type | Default | Purpose |
|------|------|---------|---------|
| `label` | `string` | — | Visible field label |
| `placeholder` | `string` | `'Naskenujte nebo zadejte kód...'` | Input hint |
| `onScan` | `(value: string) => void` | — | Called on Enter/submit |
| `loading` | `boolean` | `false` | Shows spinner, disables input |
| `uppercase` | `boolean` | `true` | Auto-uppercases typed value |
| `autoFocusOnMount` | `boolean` | `true` | Focuses input on render |
| `suppressKeyboard` | `boolean` | `false` | Hides software keyboard (scanner use) |
| `allowKeyboardToggle` | `boolean` | `false` | Shows keyboard toggle button |

## Existing consumers

- `TransportBoxCheck.tsx` — check workflow
- `TransportBoxReceive.tsx` — receive workflow

## Rationale

Physical barcode scanners emit keystrokes ending in Enter. ScanInput captures
these reliably (auto-focus, blur-refocus, uppercase normalisation) while also
supporting manual typing. All terminal screens need this behaviour; using a
raw input breaks scanner compatibility and inconsistency across screens.

## Future screens

When adding a new terminal screen (e.g. Inventura, Identifikace šarže) that
takes a code or barcode:

1. Import `ScanInput` from `../../components/terminal/ScanInput`.
2. Pass at minimum `label` and `onScan`.
3. Do **not** add a separate input element.
