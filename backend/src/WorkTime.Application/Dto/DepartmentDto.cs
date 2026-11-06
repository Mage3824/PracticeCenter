// Application-layer DTO for handing a department to the frontend. A period closure is
// configured per department (BR23.1), so the tree shape is part of the handover.
public class DepartmentDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Null for a top-level department.
    public Guid? ParentId { get; set; }
}
