### task: regenerate-api-client
Files:
- Modify (generated) `frontend/src/api/generated/api-client.ts`

Steps:
1. Follow `docs/development/api-client-generation.md`: build the backend / run the frontend build so `GetManufactureOrderResponse` exposes `availableTransitions?: ManufactureOrderState[]`.
2. Confirm with grep that the field exists in `GetManufactureOrderResponse`. Commit the generated file only if it is tracked by git.
Depends on: backend-available-transitions.

