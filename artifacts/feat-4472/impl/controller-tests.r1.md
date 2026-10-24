# controller-tests r1
Updated DashboardControllerTests: ControllerContext now has RequestServices with NullLoggerFactory; Enable/DisableTile ok tests assert OkObjectResult wrapping the handler response; added Enable/DisableTile RequiredFieldMissing -> BadRequestObjectResult tests (RequiredFieldMissing maps to 400 in ErrorCodes).
Red state verified: test project fails to compile with CS1061 ('ActionResult' has no 'Result') at the 4 new assertions, because the controller still returns ActionResult until task controller-fix. Tests go green after controller-fix.
