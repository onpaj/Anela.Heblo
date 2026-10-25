# Impl backend-handler r1

Done in a single combined unit (handler cannot compile without the DTO change, so both backend tasks were implemented together to verify).

- Request DTO: OrderDate DateTime, ExpectedDeliveryDate DateTime?
- Validator: typed rules, string parsing helpers removed
- Handler: DateTime.SpecifyKind(x.Date, Utc); string ToUtcDateTime overloads removed (no other users)
- Tests updated; new Local-kind/time-component handler test added
- dotnet build OK, dotnet format no changes, Purchase tests 434/435 (1 failure = Testcontainers/Docker unavailable, unrelated)
