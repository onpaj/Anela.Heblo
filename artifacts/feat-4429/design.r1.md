# Design: Unify TransportBoxController error handling

## Component Design
Each Pattern B action becomes:

```csharp
var response = await _mediator.Send(request, cancellationToken);
return HandleResponse(response);
```

Actions affected: `CreateNewTransportBox`, `AddItemToBox`, `RemoveItemFromBox`, `UpdateTransportBoxDescription`, `GetTransportBoxByCode`. `GetTransportBoxById` per FR-2/Decision 2. Signatures, routes and attributes unchanged.

## Data Schemas
No schema changes. Failure status mapping (from `ErrorCodes`): TransportBoxNotFound/ResourceNotFound/ManufacturedInventoryItemNotFound -> 404; TransportBoxStateChangeError/InvalidStateTransition/InsufficientStock -> 422; TransportBoxDuplicateActiveBoxFound -> 409; ValidationError/RequiredFieldMissing/TransportBoxCreationError -> 400; Exception/DatabaseError -> 500.
