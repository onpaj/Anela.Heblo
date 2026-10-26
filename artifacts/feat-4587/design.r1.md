# Design r1

No UI change. Code design:

```ts
// frontend/src/components/transport/box-detail/transportBoxEditability.ts
export type FormFieldType = "items" | "notes" | "boxNumber";
export function getIsFormEditable(state: string | undefined | null, fieldType: FormFieldType): boolean
```
Logic moved verbatim: notes -> true; New -> boxNumber; Opened -> items; else false.

TransportBoxDetail.tsx: `const isFormEditable = (fieldType) => getIsFormEditable(boxData?.transportBox?.state, fieldType);`

Test: `__tests__/transportBoxEditability.test.ts`, `it.each` over the 9 matrix rows plus extra rows for other states (InTransport, Received, Closed) and undefined/null.
