// TEST-ONLY fake (not shipped in production): in-memory implementation of ICommentRepository.
// Comment stores ReportId (not WorkDayId), so "for work day" is resolved through an injected
// report repository: Comment.ReportId -> Report.WorkDayId (constructor injection, DI style).
using WorkTime.Domain;

public class InMemoryCommentRepository : ICommentRepository
{
    private readonly Dictionary<Guid, Comment> _comments = new();

    // Resolves Comment.ReportId to the work day that owns that report.
    private readonly IReportRepository _reports;

    public InMemoryCommentRepository(IReportRepository reports)
    {
        _reports = reports;
    }

    // Un-wired repository: GetAllForWorkDayAsync returns an empty dictionary until a report
    // repository is supplied (Stage 3 wires it as a package dependency).
    public InMemoryCommentRepository() : this(null!)
    {
    }

    public Task<Comment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _comments.TryGetValue(id, out var comment);
        return Task.FromResult(comment);
    }

    // Comments of every report that belongs to the given working day.
    public async Task<Dictionary<Guid, Comment>> GetAllForWorkDayAsync(Guid workDayId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Comment>();

        if (_reports == null)
        {
            return result;
        }

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
