# Design
No UI, API or data-model changes. Single expression replacement:

```csharp
- var returnedQuantity = (int)(ingredient.RequiredQuantity * quantity);
+ var returnedQuantity = ConsumedQuantity(ingredient, quantity);
```
Optional: leave the helper XML comment as is (it already says both paths go through it; disassembly now does too).
