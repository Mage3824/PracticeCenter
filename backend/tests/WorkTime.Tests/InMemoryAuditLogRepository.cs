// TEST-ONLY fake (not shipped in production): in-memory implementation of IAuditLogRepository.
// Entries are addressed by their own Id and read back by the entity they changed (BR30).
using WorkTime.Domain;

public class InMemoryAuditLogRepository : IAuditLogRepository
{
    private readonly Dictionary<Guid, AuditLog> _auditLogs = new();

    public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _auditLogs.TryGetValue(id, out var auditLog);
        return Task.FromResult(auditLog);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
    public Task<Dictionary<Guid, AuditLog>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, AuditLog>();

        foreach (var id in ids.Keys)
        {
            _auditLogs.TryGetValue(id, out var auditLog);
            if (auditLog != null)
            {
                result[auditLog.Id] = auditLog;
            }
        }

        return Task.FromResult(result);
    }

    // BR30/BR29: the entries written about one entity, so a service can prove that an
    // action was logged (BR3 "Auto-finish") without being handed log ids.
    public async Task<IEnumerable<AuditLog>> GetHistoryForEntityAsync(Guid entityId, CancellationToken cancellationToken)
    {
        var result = new List<AuditLog>();

        foreach (var entry in _auditLogs)
        {
            if (entry.Value.EntityId == entityId)
            {
                result.Add(entry.Value);
            }
        }

        return result;
    }

    public Task SaveAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        _auditLogs[auditLog.Id] = auditLog;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _auditLogs.Remove(id);
        return Task.CompletedTask;
    }
}
