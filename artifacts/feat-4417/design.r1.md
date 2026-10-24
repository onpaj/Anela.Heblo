# Design: GetInvoiceDetails error propagation

## Component Design
`GetInvoiceDetailsHandler.Handle` becomes:
1. Log info "Getting details for invoice {InvoiceId}".
2. `var invoice = await _invoicesClient.GetInvoiceByIdAsync(request.InvoiceId);`
3. Map via `_mapper` only when non-null (keep the explicit null-check comment).
4. Return `GetInvoiceDetailsResponse { Invoice = mapped, Found = invoice != null }`.
No try/catch. Constructor and fields unchanged. Controller unchanged.

## Data Schemas
No schema or API shape changes. `GetInvoiceDetailsResponse { ReceivedInvoiceDto? Invoice; bool Found }` stays as is.
