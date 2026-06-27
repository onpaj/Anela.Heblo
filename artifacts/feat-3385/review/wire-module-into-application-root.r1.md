# Code Review: wire-module-into-application-root

## Summary
Both acceptance criteria are satisfied. The `using Anela.Heblo.Application.Features.BackgroundRefresh;` directive appears on line 11 of `ApplicationModule.cs`, and `services.AddBackgroundRefreshModule()` is called on line 81, directly after `services.AddBackgroundJobsModule()` on line 80. The registration ordering is correct and follows the established pattern used by all other feature modules in this file.

## Review Result: PASS

### task: wire-module-into-application-root
**Status:** PASS

## Overall Notes
The implementation is clean and minimal — exactly the surgical change the task required. The using directive is placed in natural alphabetical order within the `Features.*` using block (between `BackgroundJobs` and `Bank`), and the `AddBackgroundRefreshModule()` call is immediately adjacent to `AddBackgroundJobsModule()` as specified. No unrelated code was touched.
