using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public class PersonMonthComputation
{
    public Guid PersonId { get; set; }
    public decimal? DailyContractHours { get; set; }
    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class OvertimeCalculationService
{
    private readonly ILogetoClient _client;
    private readonly IContractHoursProvider _contractHours;
    private readonly IOptions<OvertimeOptions> _options;
    private readonly ILogger<OvertimeCalculationService> _logger;

    public OvertimeCalculationService(
        ILogetoClient client,
        IContractHoursProvider contractHours,
        IOptions<OvertimeOptions> options,
        ILogger<OvertimeCalculationService> logger)
    {
        _client = client;
        _contractHours = contractHours;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PersonMonthComputation>> ComputeMonthAsync(
        int year, int month, IReadOnlyList<OvertimeEmployee> employees, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var activities = await _client.GetActivitiesAsync(cancellationToken);
        var entries = await _client.GetTimeTrackingAsync(monthStart, monthEnd, cancellationToken);

        var categoryByActivity = BuildCategoryMap(activities);
        var nameByActivity = activities.ToDictionary(a => a.Guid, a => a.Name ?? a.Guid.ToString());
        var entriesByPerson = entries.GroupBy(e => e.Person).ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<PersonMonthComputation>();

        foreach (var employee in employees)
        {
            if (employee.BaselineDate > monthEnd)
            {
                continue;
            }

            var effectiveStart = employee.BaselineDate > monthStart ? employee.BaselineDate : monthStart;
            var row = new PersonMonthComputation { PersonId = employee.PersonId };
            var credited = 0m;

            var personEntries = entriesByPerson.TryGetValue(employee.PersonId, out var list)
                ? list.Where(e => e.Date >= effectiveStart)
                : Enumerable.Empty<LogetoTimeEntry>();

            foreach (var entry in personEntries)
            {
                var category = categoryByActivity.TryGetValue(entry.Activity, out var c)
                    ? c
                    : OvertimeActivityCategory.Other;

                if (category == OvertimeActivityCategory.Break)
                {
                    continue;
                }

                var activityName = nameByActivity.TryGetValue(entry.Activity, out var n) ? n : entry.Activity.ToString();
                var hours = GetEntryHours(entry, activityName, row.Warnings);

                if (category == OvertimeActivityCategory.Other)
                {
                    row.Warnings.Add($"Nezařazená aktivita: {activityName} ({entry.Date:yyyy-MM-dd})");
                }

                switch (category)
                {
                    case OvertimeActivityCategory.Work:
                        row.WorkedHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Vacation:
                        row.VacationHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Sick:
                        row.SickHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Doctor:
                        row.DoctorHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Ocr:
                        row.OtherAbsenceHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.CompTime:
                        row.CompTimeHours += hours;   // visible, never credited
                        break;
                    case OvertimeActivityCategory.Other:
                        row.OtherAbsenceHours += hours;   // visible, never credited
                        break;
                }
            }

            var daily = await _contractHours.GetDailyHoursAsync(employee.PersonId, year, month, cancellationToken);
            row.DailyContractHours = daily;
            if (daily is null)
            {
                row.Warnings.Add("Chybí úvazek");
            }
            else
            {
                var workingDays = WorkingDaysCalculator.CountWorkingDays(effectiveStart, monthEnd);
                row.RequiredHours = Round(workingDays * daily.Value);
            }

            row.WorkedHours = Round(row.WorkedHours);
            row.VacationHours = Round(row.VacationHours);
            row.SickHours = Round(row.SickHours);
            row.DoctorHours = Round(row.DoctorHours);
            row.CompTimeHours = Round(row.CompTimeHours);
            row.OtherAbsenceHours = Round(row.OtherAbsenceHours);
            row.DeltaHours = Round(credited) - row.RequiredHours;

            results.Add(row);
        }

        return results;
    }

    private Dictionary<Guid, OvertimeActivityCategory> BuildCategoryMap(IReadOnlyList<LogetoActivity> activities)
    {
        var map = new Dictionary<Guid, OvertimeActivityCategory>();
        var configured = _options.Value.ActivityCategories;

        foreach (var activity in activities)
        {
            if (activity.Name is not null
                && configured.TryGetValue(activity.Name, out var categoryName)
                && Enum.TryParse<OvertimeActivityCategory>(categoryName, ignoreCase: true, out var mapped))
            {
                map[activity.Guid] = mapped;
            }
            else if (activity.Type == LogetoActivityTypes.Break)
            {
                map[activity.Guid] = OvertimeActivityCategory.Break;
            }
            else if (activity.Type == LogetoActivityTypes.Work)
            {
                map[activity.Guid] = OvertimeActivityCategory.Work;
            }
            else
            {
                map[activity.Guid] = OvertimeActivityCategory.Other;
            }
        }

        return map;
    }

    private static decimal GetEntryHours(LogetoTimeEntry entry, string activityName, List<string> warnings)
    {
        if (entry.From.HasValue && entry.To.HasValue)
        {
            return (decimal)(entry.To.Value - entry.From.Value).TotalHours;
        }

        if (entry.From.HasValue && !entry.To.HasValue)
        {
            warnings.Add($"Neuzavřený záznam: {entry.Date:yyyy-MM-dd} {activityName}");
            return 0m;
        }

        if (!string.IsNullOrWhiteSpace(entry.Hours) && TimeSpan.TryParse(entry.Hours, out var span))
        {
            return (decimal)span.TotalHours;
        }

        warnings.Add($"Záznam bez hodin: {entry.Date:yyyy-MM-dd} {activityName}");
        return 0m;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
