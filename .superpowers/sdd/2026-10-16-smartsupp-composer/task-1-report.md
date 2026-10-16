# Task 1 report
Docs only. Changed:
- docs/integrations/smartsupp-api.md: replaced "Response (assumed)" with full-message-object note; added sections for POST messages with attachments, GET /files/upload/token, POST <upload url>, Message text formatting (no bold, html flattened, auto-link, emoji).
- docs/integrations/smartsupp-webhook.md: added verified fragments for contact_replied (content.type upload, content.data) and agent_replied (attachments incl. thumb_400); states customer files never appear in attachments.
Concerns: spec has no full webhook envelopes, so samples are data.message fragments (no invented fields). Existing webhook doc has older "content" as a plain string in samples; left untouched (surgical).
