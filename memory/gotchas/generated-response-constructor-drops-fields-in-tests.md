# `new GeneratedResponse({...})` silently drops fields in frontend tests

## Symptom

A test builds a fixture from a generated API class, e.g.

```ts
const response = new GetPriceDivergenceReportResponse({ rows: [...], summary: {...} });
```

and every field beyond `success` / `errorCode` / `params` arrives as `undefined` at the
component. No error, no type complaint — the assertion just fails as though the component
ignored the data, sending you hunting through the component instead of the fixture.

## Root cause

The generated classes declare their properties as TypeScript class fields and populate them
in `init(data)`. Under this project's Babel class-fields transform (`useDefineForClassFields`
semantics), the field declarations are re-emitted as `defineProperty` writes that run **after**
the constructor body, so they overwrite whatever `init()` just assigned — resetting everything
to `undefined`. The three fields that survive are the ones defined on `BaseResponse` rather
than the derived class.

## Fix

Use the generated static factory instead of the constructor:

```ts
const response = GetPriceDivergenceReportResponse.fromJS({ rows: [...], summary: {...} });
```

`fromJS` constructs and then calls `init()`, so the assignments happen last and survive.

## Applies to

Any generated `*Response` / `*Dto` class used as a test fixture — this is not specific to
pricing. Found while writing `PriceDivergenceReport` tests (2026-09-10); the same trap would
hit any suite that hand-builds generated response objects.

Related: [dto-records-break-openapi-generation.md](dto-records-break-openapi-generation.md),
[api-client-must-use-absolute-urls.md](api-client-must-use-absolute-urls.md)
