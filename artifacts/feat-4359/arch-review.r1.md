# Architecture review
Pure source hygiene; no DI registrations, contracts or runtime behaviour change. Removing the import reduces the apparent Invoices -> Bank coupling and aligns with module boundary rules in docs/architecture/development_guidelines.md. Risk: none; the compiler will flag any real dependency. Approved.
