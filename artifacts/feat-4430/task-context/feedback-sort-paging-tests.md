### task: feedback-sort-paging-tests
**Files:** Modify the same test file.

- [ ] Tests (Theory or Facts): sortBy `PrecisionScore` asc/desc and `StyleScore` asc/desc over `hasFeedback=true` articles with distinct non-null scores; default sort (`CreatedAt` and unknown value `"Bogus"`) asc/desc by CreatedAt.
- [ ] Paging test: page 2, pageSize 2 over 5 articles returns items 3-4 in order, TotalCount stays 5.
- [ ] Run the filtered test command; expect pass.
- [ ] Commit: `test(article): cover feedback sort and paging`.

