The arch-review of 2026-05-25 on \`useSubmitArticleFeedbackMutation\` removed
an \`as any\` bypass by introducing \`getApiBaseUrl()\` and
\`getAuthenticatedFetch()\` helpers in \`frontend/src/api/client.ts\` so the
hook can issue a raw \`fetch\` and treat HTTP 409 as a typed
\`{ alreadySubmitted: true }\` result rather than an exception.

The long-term fix is to configure the NSwag template (or the endpoint
annotations on the C# side) so that the generated client method for
\`POST /api/articles/{articleId}/feedback\` returns a discriminated-union
result type that includes 409 as a named branch — eliminating the need
for the helper-based raw fetch at this call site.

When that lands, the helpers may still be useful for future endpoints
with similar shapes (e.g. 412 precondition-failed) — so they can stay,
but this specific hook should switch back to a fully typed
\`apiClient.articles_SubmitFeedback(...)\` call.

Tags: feat-arch-review-article-usesubmitarticlefeed 2026-05-25.

Audit follow-up: grep \`apiClient as any\` across \`frontend/src/api/hooks/\`
to identify any other hooks still using the same pattern and either
refactor them onto the new helpers or include them in this issue's scope.