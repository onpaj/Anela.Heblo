using Anela.Heblo.Domain.Features.Attendance.Overtime;
using ClosedXML.Excel;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>Builds the shared "Evidence přesčasů" workbook: one sheet per closed month,
/// mirroring the legacy internal Excel's columns.</summary>
public class OvertimeExcelBuilder
{
    private static readonly string[] Headers =
    {
        "Zaměstnanec", "Převod z minula", "Úvazek (h)", "Odpracováno", "Dovolená",
        "Nemoc", "Lékař", "Náhradní volno", "Ostatní", "Rozdíl", "Korekce (h)",
        "Korekce – detail", "Nový zůstatek"
    };

    public byte[] Build(
        IReadOnlyList<OvertimeEmployee> employees,
        IReadOnlyList<OvertimeMonthlyStatement> closedStatements,
        IReadOnlyList<OvertimeAdjustment> adjustments)
    {
        using var workbook = new XLWorkbook();
        var nameByPerson = employees.ToDictionary(e => e.PersonId, e => e.DisplayName);

        var months = closedStatements
            .Where(s => s.Status == OvertimeStatementStatus.Closed)
            .GroupBy(s => (s.Year, s.Month))
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .ToList();

        if (months.Count == 0)
        {
            var info = workbook.AddWorksheet("Info");
            info.Cell(1, 1).Value = "Zatím není uzavřen žádný měsíc.";
        }

        foreach (var monthGroup in months)
        {
            var sheet = workbook.AddWorksheet($"{monthGroup.Key.Year}-{monthGroup.Key.Month:D2}");

            for (var i = 0; i < Headers.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = Headers[i];
                sheet.Cell(1, i + 1).Style.Font.Bold = true;
            }

            var row = 2;
            foreach (var statement in monthGroup.OrderBy(s => nameByPerson.TryGetValue(s.PersonId, out var n) ? n : ""))
            {
                var personAdjustments = adjustments
                    .Where(a => a.PersonId == statement.PersonId && a.Year == statement.Year && a.Month == statement.Month)
                    .ToList();
                var adjustmentsTotal = personAdjustments.Sum(a => a.Hours);
                var detail = string.Join("; ", personAdjustments.Select(a => $"{a.Type}: {a.Hours}h – {a.Note}"));

                sheet.Cell(row, 1).Value = nameByPerson.TryGetValue(statement.PersonId, out var name) ? name : statement.PersonId.ToString();
                sheet.Cell(row, 2).Value = statement.BalanceAfter - statement.DeltaHours - adjustmentsTotal;
                sheet.Cell(row, 3).Value = statement.RequiredHours;
                sheet.Cell(row, 4).Value = statement.WorkedHours;
                sheet.Cell(row, 5).Value = statement.VacationHours;
                sheet.Cell(row, 6).Value = statement.SickHours;
                sheet.Cell(row, 7).Value = statement.DoctorHours;
                sheet.Cell(row, 8).Value = statement.CompTimeHours;
                sheet.Cell(row, 9).Value = statement.OtherAbsenceHours;
                sheet.Cell(row, 10).Value = statement.DeltaHours;
                sheet.Cell(row, 11).Value = adjustmentsTotal;
                sheet.Cell(row, 12).Value = detail;
                sheet.Cell(row, 13).Value = statement.BalanceAfter;
                row++;
            }

            sheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
