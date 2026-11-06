// Формы запросов эндпоинтов управления (пользователи, отделы, комментарии, период).
// Валидация — DataAnnotations: невалидное тело отдаёт 400 как ValidationProblemDetails (RFC 7807).
using System.ComponentModel.DataAnnotations;

namespace WorkTime.Api;

public class CreateUserRequest
{
    [Required]
    public string? login { get; set; }

    [Required]
    public string? fullName { get; set; }

    // BR34: пароль приходит только в запросе и никогда не возвращается в ответах.
    [Required]
    public string? password { get; set; }

    // Токен роли как в Enums.cs (Admin / Manager / Employee).
    [Required]
    public string? role { get; set; }

    [Required]
    public Guid? departmentId { get; set; }
}

public class UpdateUserRequest
{
    public string? fullName { get; set; }

    public string? role { get; set; }

    public Guid? departmentId { get; set; }

    // BR24: снятие доступа без удаления учётной записи; true возвращает доступ.
    public bool? isActive { get; set; }
}

public class CreateDepartmentRequest
{
    [Required]
    public string? name { get; set; }

    public Guid? parentId { get; set; }
}

public class UpdateDepartmentRequest
{
    public string? name { get; set; }

    public Guid? parentId { get; set; }
}

public class CreateCommentRequest
{
    [Required]
    public Guid? reportId { get; set; }

    [Required]
    public string? text { get; set; }
}

// BR23.2: вскрытия закрытого периода требует причину / номер акта.
public class ReopenPeriodRequest
{
    [Required]
    public string? reason { get; set; }
}
