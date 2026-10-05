# Review: use-redactor-in-handler-and-adapter (r1)
Checked diff against task-context: handler switched to shared redactor and dead method removed; adapter logs redacted URL on both log sites while request URL is unchanged; tests cover success and failure paths and fail if the raw URL is logged. Build, FileStorage tests (134) and format pass.

**Status:** PASS
