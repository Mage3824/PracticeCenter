// Domain-level exception hierarchy: business-rule violations and missing entities.

public class BusinessRuleException : Exception
{
    public string Code { get; set; }

    public BusinessRuleException(string code, string message) : base(message)
    {
        this.Code = code;
    }
}

public class EntityNotFoundException : Exception
{
    public Guid EntityId { get; set; }

    public string EntityType { get; set; }

    public EntityNotFoundException(Guid entityId, string entityType) 
        : base($"Entity of type '{entityType}' with id '{entityId}' was not found.")
    {
        this.EntityId = entityId;
        this.EntityType = entityType;
    }
}