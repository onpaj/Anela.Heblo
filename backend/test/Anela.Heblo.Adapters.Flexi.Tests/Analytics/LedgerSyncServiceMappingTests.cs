using Anela.Heblo.Adapters.Flexi.Analytics;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

/// <summary>
/// Mapping tests built from the wire shape FlexiBee actually returns for
/// <c>ucetni-denik</c>, captured against the live company file on 2026-09-22 with the
/// exact detail/includes the SDK's LedgerRequest sends.
///
/// The rest of the ledger suite builds DTOs field-by-field with plausible-looking values
/// (Id = 1, DebitAccountShowAs = "501000"). FlexiBee populates neither: <c>ucetni-denik</c>
/// is a view, so <c>id</c> comes back as -1 on every row, and asking for
/// <c>mdUcet(kod,nazev,id)</c> plus <c>includes=/ucetni-denik/mdUcet</c> returns a nested
/// array instead of the <c>mdUcet@showAs</c> scalar. Those hand-built DTOs are why the
/// mapping shipped broken and unit-green.
/// </summary>
public class LedgerSyncServiceMappingTests
{
    /// <summary>
    /// The one row in this file that is NOT hand-built. Every other test here starts from a DTO
    /// whose properties are already populated, which exercises <c>Map</c> but proves nothing about
    /// the JSON bindings -- and the bindings are exactly what was broken: <c>idUcetniDenik</c>
    /// carrying the identity, and <c>mdUcet</c>/<c>dalUcet</c>/<c>stredisko</c>/<c>mena</c>
    /// arriving as nested arrays rather than <c>@showAs</c> scalars. Those attribute names live in
    /// the transitive Rem.FlexiBeeSDK.Model package, so an SDK bump can silently change them and
    /// every hand-built-DTO test in this file stays green while production writes nulls.
    ///
    /// Verbatim response body for one row, captured from the live company file on 2026-09-22.
    /// </summary>
    private const string RealWorldResponseJson = """
        {
          "id": -1,
          "idUcetniDenik": "19327896679",
          "datUcto": "2026-06-01+02:00",
          "doklad": "PF260567",
          "nazFirmy": "JP Digital,s.r.o.",
          "popis": "správa reklam na Meta",
          "sumTuz": 46500.0,
          "kurz": 1.0,
          "stredisko": [ { "id": 5, "kod": "MARKETING", "nazev": "Marketing" } ],
          "mena": [ { "id": 31, "kod": "CZK" } ],
          "mdUcet": [ { "id": 121, "kod": "518033", "nazev": "Marketing-Performance" } ],
          "dalUcet": [ { "id": 60, "kod": "321001", "nazev": "Dodavatelé" } ],
          "idDokl": 107531,
          "idDokl@evidencePath": "faktura-prijata",
          "postingPeriod": "2026/06",
          "firma@ref": "/c/anela/adresar/1152.json",
          "firma@showAs": "JP: JP Digital,s.r.o.",
          "lastUpdate": "2026-09-01T04:15:13.000+02:00"
        }
        """;

    [Fact]
    public void Map_FromTheRawResponseBody_FillsTheColumnsTheViewsReadFrom()
    {
        var dto = JsonConvert.DeserializeObject<LedgerItemFlexiDto>(RealWorldResponseJson)!;

        var entry = LedgerSyncService.Map(dto);

        // The identity lives in idUcetniDenik, never in `id` -- ucetni-denik is a view.
        entry.FlexiId.Should().Be(19327896679);
        // Nested arrays, not @showAs scalars. These four were null in production.
        entry.AccountDebit.Should().Be("518033");
        entry.AccountCredit.Should().Be("321001");
        entry.CostCenter.Should().Be("MARKETING");
        entry.Currency.Should().Be("CZK");
        // Mapped for the first time by this PR.
        entry.Period.Should().Be("2026/06");
        entry.DocumentType.Should().Be("faktura-prijata");
        entry.Contact.Should().Be("JP: JP Digital,s.r.o.");
        entry.Amount.Should().Be(46500.0m);
    }

    [Fact]
    public void RawPayload_IsAlwaysValidJson()
    {
        // raw_payload is a jsonb column; EF InMemory accepts any string, so nothing else in the
        // suite would notice the serializer settings producing something Postgres rejects on the
        // first 500-row batch.
        var dto = JsonConvert.DeserializeObject<LedgerItemFlexiDto>(RealWorldResponseJson)!;

        FluentActions.Invoking(() => JToken.Parse(LedgerSyncService.Map(dto).RawPayload!))
            .Should().NotThrow();
        FluentActions.Invoking(() => JToken.Parse(LedgerSyncService.Map(new LedgerItemFlexiDto
        {
            Id = -1,
            JournalId = "1",
            AccountingDate = new DateTime(2026, 1, 1),
        }).RawPayload!)).Should().NotThrow();
    }

    /// <summary>
    /// One real row, transcribed from the live response:
    /// a received invoice for Meta ad management, posted to the MARKETING cost centre.
    /// </summary>
    private static LedgerItemFlexiDto RealWorldDto() => new()
    {
        // Always -1 on this evidence: 'id' is meaningless for a view.
        Id = -1,
        // The real identity, returned as a string in the 'idUcetniDenik' property.
        JournalId = "19327896679",
        ParSymbol = "126019027",
        AccountingDate = new DateTime(2026, 6, 1),
        Document = "PF260567",
        CompanyName = "JP Digital,s.r.o.",
        DepartmentList = [new DepartmentFlexiDto { Id = 5, Code = "MARKETING", Name = "Marketing" }],
        Description = "správa reklam na Meta",
        AmountLocal = 46500.0,
        Currency = [new CurrencyFlexiDto { Id = 31, Code = "CZK" }],
        DebitAccountList = [new AccountFlexiDto { Id = 121, Code = "518033", Name = "Marketing-Performance" }],
        CreditAccountList = [new AccountFlexiDto { Id = 60, Code = "321001", Name = "Dodavatelé" }],
        DocumentId = 107531,
        DocumentIdEvidencePath = "faktura-prijata",
        Period = "2026/06",
        ContactRef = "/c/anela/adresar/1152.json",
        ContactShowAs = "JP: JP Digital,s.r.o.",
        LastUpdate = new DateTimeOffset(2026, 9, 1, 4, 15, 13, TimeSpan.FromHours(2)),
    };

    [Fact]
    public void Map_TakesTheKeyFromIdUcetniDenik_NotFromTheAlwaysMinusOneIdField()
    {
        // flexi_id is the primary key. Mapping dto.Id would give every single row -1 and the
        // first batch's SaveChanges would die on a duplicate key, writing nothing at all.
        var entry = LedgerSyncService.Map(RealWorldDto());

        entry.FlexiId.Should().Be(19327896679L);
    }

    [Fact]
    public void Map_ReadsAccountsCostCentreAndCurrencyFromTheNestedArrays()
    {
        var entry = LedgerSyncService.Map(RealWorldDto());

        entry.AccountDebit.Should().Be("518033");
        entry.AccountCredit.Should().Be("321001");
        entry.CostCenter.Should().Be("MARKETING");
        entry.Currency.Should().Be("CZK");
    }

    [Fact]
    public void Map_PopulatesPeriodDocumentTypeAndContact()
    {
        // These three carry backlog items #31-#33 (ad spend arrives as received invoices, so
        // document_type = 'faktura-prijata' plus the supplier is what identifies it) and the
        // month grain every read view groups on.
        var entry = LedgerSyncService.Map(RealWorldDto());

        entry.Period.Should().Be("2026/06");
        entry.DocumentType.Should().Be("faktura-prijata");
        entry.Contact.Should().Be("JP: JP Digital,s.r.o.");
    }

    [Fact]
    public void Map_FallsBackToCompanyNameWhenTheContactHasNoShowAs()
    {
        var dto = RealWorldDto();
        dto.ContactShowAs = null;

        LedgerSyncService.Map(dto).Contact.Should().Be("JP Digital,s.r.o.");
    }

    [Fact]
    public void Map_LeavesContactNullWhenTheRowCarriesNoCounterparty()
    {
        // 95% of rows (644 396 of 679 613 on 2026-09-22) have no firma — internal postings,
        // stock movements and bank fees. Empty strings must not become a "" grouping bucket.
        var dto = RealWorldDto();
        dto.ContactShowAs = null;
        dto.CompanyName = "";

        LedgerSyncService.Map(dto).Contact.Should().BeNull();
    }

    [Fact]
    public void Map_ToleratesRowsWithNoDepartmentAccountOrCurrency()
    {
        // LedgerItemFlexiDto.Department/DebitAccount/CreditAccount are List.First() wrappers and
        // throw on an empty or null list, so the mapping has to go through the lists directly.
        var dto = RealWorldDto();
        dto.DepartmentList = null;
        dto.DebitAccountList = [];
        dto.CreditAccountList = null;
        dto.Currency = [];

        var entry = LedgerSyncService.Map(dto);

        entry.CostCenter.Should().BeNull();
        entry.AccountDebit.Should().BeNull();
        entry.AccountCredit.Should().BeNull();
        entry.Currency.Should().BeNull();
        entry.FlexiId.Should().Be(19327896679L);
    }

    [Fact]
    public void Map_KeepsTheWholePayloadSoAMissingDimensionCanBeRecoveredWithoutAReSync()
    {
        var entry = LedgerSyncService.Map(RealWorldDto());

        entry.RawPayload.Should().Contain("19327896679");
        entry.RawPayload.Should().Contain("518033");
        entry.RawPayload.Should().Contain("faktura-prijata");
    }

    [Fact]
    public void Map_LeavesAccountingTemplateNull_BecauseUcetniDenikDoesNotCarryIt()
    {
        // Documented finding, not an oversight: the evidence exposes 38 properties and none of
        // them is the předkontace. The assignment stays so the column lights up by itself if a
        // future SDK/FlexiBee version starts returning one.
        LedgerSyncService.Map(RealWorldDto()).AccountingTemplate.Should().BeNull();
    }
}
