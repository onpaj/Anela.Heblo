## Module
Transport Boxes (Logistics)

## Finding
`frontend/src/api/hooks/useTransportBoxTransitions.ts` exports `useAllowedTransitionsQuery`, which builds a request to `GET /api/transport-boxes/{boxId}/allowed-transitions` via a raw `apiClient.http.fetch` call.

- `TransportBoxController.cs`'s route table (`[Route("api/transport-boxes")]` + its `HttpGet`/`HttpPost`/`HttpPut`/`HttpDelete` actions) has **no `allowed-transitions` action** — grepping the Logistics Application tree for `allowed-transitions`/`AllowedTransitions` finds only the `TransportBoxDto.AllowedTransitions` field, populated inline by `TransportBoxMappingProfile` from `TransitionNode.GetAllTransitions()` on every normal box fetch — never a standalone endpoint.
- `useAllowedTransitionsQuery` / `useTransportBoxTransitions.ts` has **zero importers** anywhere in `frontend/src` or `frontend/test` (verified via repo-wide grep).
- The data it duplicates already works and is consumed elsewhere: `TransportBoxActions.tsx` reads `allowedTransitions` off the box DTO returned by the normal `GetTransportBoxById`/`GetTransportBoxes` calls.

## Why it matters
The file is unreferenced dead code, and it would fail immediately (404) if anyone ever wired it up — e.g. by copy-pasting it as a template for a new feature, a plausible failure mode since the file otherwise reads like a normal, working hook (typed response interfaces, React Query wiring, no obvious red flags). It also duplicates a request shape that the working mechanism (the inline `AllowedTransitions` DTO field) already provides.

## Suggested direction
Delete `useTransportBoxTransitions.ts` — the box DTO's inline `allowedTransitions` field, already consumed by `TransportBoxActions.tsx`, is the working path.

