# Design: OrgChart wiring regression test (#4471)

## Component Design
Single new xUnit test class `OrgChartModuleWiringTests` (namespace `Anela.Heblo.Tests.Features.OrgChart`) with one `[Fact]`:
`AddApplicationServices_registers_OrgChartOptions_validation`.
Steps: build `ConfigurationBuilder` with in-memory values; create `ServiceCollection`; call `services.AddApplicationServices(config, env)`; assert a descriptor with `ServiceType == typeof(IValidateOptions<OrgChartOptions>)` exists, and (stronger, if the provider builds) that `provider.GetRequiredService<IOptions<OrgChartOptions>>().Value` throws `OptionsValidationException` with message containing `DataSourceUrl`.

## Data Schemas
None.
