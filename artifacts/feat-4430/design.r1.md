# Design: ArticleRepository feedback query tests

## Component Design
Single test class `ArticleRepositoryFeedbackQueryTests : IDisposable`, with a `Seed(...)` helper creating an `Article` (Topic required, CreatedAt explicit, RequestedBy, PrecisionScore, StyleScore). Fixture set: A (both scores, user1, t1), B (precision only, user1, t2), C (style only, user2, t3), D (none, user2, t4), E (none, user1, t5).

## Data Schemas
No schema changes. Expected: hasFeedback=true -> {A,B,C}; false -> {D,E}; requestedBy user1 -> {A,B,E}; stats total 5, withFeedback 3.
