// TEST-ONLY fake (not shipped in production): in-memory implementation of IReportRepository.
// No cross-repository lookups: Report is addressed by its own Id and by the WorkDayId it
// describes, both stored on the entity itself.
using WorkTime.Domain;

public class InMemoryReportRepository : IReportRepository
{
    private readonly Dictionary<Guid, Report> _reports = new();

    public Task<Report?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _reports.TryGetValue(id, out var report);
        return Task.FromResult(report);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
    public Task<Dictionary<Guid, Report>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Report>();

        foreach (var id in ids.Keys)
        {
            _reports.TryGetValue(id, out var report);
            if (report != null)
            {
                result[report.Id] = report;
            }
        }

        return Task.FromResult(result);
    }

    // BR14: at most one report per working day, so the service can find and update it
    // without being handed a report id. Null when the day has no report yet.
    public Task<Report?> GetForWorkDayAsync(Guid workDayId, CancellationToken cancellationToken)
    {
        foreach (var entry in _reports)
        {
            if (entry.Value.WorkDayId == workDayId)
            {
                return Task.FromResult<Report?>(entry.Value);
            }
        }

        return Task.FromResult((Report?)null);
    }

    public Task SaveAsync(Report report, CancellationToken cancellationToken)
    {
        _reports[report.Id] = report;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _reports.Remove(id);
        return Task.CompletedTask;
    }
}
