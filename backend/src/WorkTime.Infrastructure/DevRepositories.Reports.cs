// Dev-tier ("in-memory") repositories for the WorkTime.Api (Stage 2) - see DevRepositories.Users.cs
// for the layer note.
using WorkTime.Domain;

namespace WorkTime.Infrastructure;

// In-memory implementation of IReportRepository. No cross-repository lookups: Report is addressed
// by its own Id and by the WorkDayId it describes, both stored on the entity itself.
public class InMemoryReportRepository : IReportRepository
{
    private readonly Dictionary<Guid, Report> _reports = new();

    public Task<Report?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _reports.TryGetValue(id, out var report);
        return Task.FromResult(report);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
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

    // BR14: at most one report per working day, so the service can find and update it without
    // being handed a report Id. Null when the day has no report yet.
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

    
    // Возвращает копию списка отчётов
    public Task<IEnumerable<Report>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        IEnumerable<Report> result = _reports.Values.ToList();
        return Task.FromResult(result);
    }

}

// In-memory implementation of ICommentRepository. Comment stores ReportId (not WorkDayId), so
// "comments of a work day" is resolved through an injected report repository:
// Comment.ReportId -> Report.WorkDayId (DI style).
public class InMemoryCommentRepository : ICommentRepository
{
    private readonly Dictionary<Guid, Comment> _comments = new();

    // Resolves Comment.ReportId to the work day that owns that report.
    private readonly IReportRepository _reports;

    public InMemoryCommentRepository(IReportRepository reports)
    {
        _reports = reports;
    }

    public Task<Comment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _comments.TryGetValue(id, out var comment);
        return Task.FromResult(comment);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
    public Task<Dictionary<Guid, Comment>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Comment>();

        foreach (var id in ids.Keys)
        {
            _comments.TryGetValue(id, out var comment);
            if (comment != null)
            {
                result[comment.Id] = comment;
            }
        }

        return Task.FromResult(result);
    }

    // Comments of every report that belongs to the given working day.
    public async Task<Dictionary<Guid, Comment>> GetAllForWorkDayAsync(
        Guid workDayId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Comment>();

        foreach (var entry in _comments)
        {
            var comment = entry.Value;
            var report = await _reports.GetByIdAsync(comment.ReportId, cancellationToken);
            if (report != null && report.WorkDayId == workDayId)
            {
                result[comment.Id] = comment;
            }
        }

        return result;
    }

    public Task SaveAsync(Comment comment, CancellationToken cancellationToken)
    {
        _comments[comment.Id] = comment;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _comments.Remove(id);
        return Task.CompletedTask;
    }
}
