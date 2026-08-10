using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using ClosedXML.Excel;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeExcelBuilderTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Build_CreatesSheetPerClosedMonth_WithBalanceColumns()
    {
        var employees = new List<OvertimeEmployee>
        {
            new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m }
        };
        var statements = new List<OvertimeMonthlyStatement>
        {
            new()
            {
                PersonId = Person, Year = 2026, Month = 9, Status = OvertimeStatementStatus.Closed,
                RequiredHours = 134.4m, WorkedHours = 130m, VacationHours = 6.4m,
                DeltaHours = 2m, BalanceAfter = 3.5m
            }
        };
        var adjustments = new List<OvertimeAdjustment>
        {
            new() { PersonId = Person, Year = 2026, Month = 9, Type = OvertimeAdjustmentType.Payout, Hours = -1m, Note = "Prémie" }
        };

        var bytes = new OvertimeExcelBuilder().Build(employees, statements, adjustments);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheets.Should().ContainSingle(ws => ws.Name == "2026-09");
        var sheet = workbook.Worksheet("2026-09");
        sheet.Cell(1, 1).GetString().Should().Be("Zaměstnanec");
        sheet.Cell(2, 1).GetString().Should().Be("Pepina");
        sheet.Cell(2, 13).GetValue<decimal>().Should().Be(3.5m);   // Nový zůstatek
        sheet.Cell(2, 12).GetString().Should().Contain("Prémie");  // Korekce – detail
    }

    [Fact]
    public void Build_WithNoClosedMonths_ProducesInfoSheet()
    {
        var bytes = new OvertimeExcelBuilder().Build(
            new List<OvertimeEmployee>(), new List<OvertimeMonthlyStatement>(), new List<OvertimeAdjustment>());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheets.Count.Should().Be(1);   // placeholder sheet, valid file
    }
}
