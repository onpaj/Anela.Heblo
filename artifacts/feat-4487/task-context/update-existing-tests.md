### task: update-existing-tests
- `GetUserSettingsHandlerTests`: rewrite as pure query tests (mock reader; verify user id resolution incl. anonymous); write-path cases already moved to provisioner tests.
- `GetTileDataHandlerTests`: mock `IUserDashboardSettingsReader` instead of `IMediator`.
- `Infrastructure/UserDashboardSettingsMutatorTests`: mock provisioner instead of `IMediator`; assert provisioner called before lock acquired and with the resolved user id.
- Check `EnableTileHandlerTests`, `DisableTileHandlerTests`, `SaveUserSettingsHandlerTests`, `DashboardControllerAuthorizationTests` still compile/pass.
Depends on: rewire-consumers.

