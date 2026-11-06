// Request bodies of WorkTime.Api (Stage 2). These are the "wire models" of the request side: their
// property names are exactly the JSON keys of api-contracts.md §5/§7, and they deliberately carry no
// employeeId / currentUserId / editorId / actorId field - identity always comes from the token subject
// (see api-contracts.md §6 "the client sends criteria, never a list of ids").
//
// Values travel as contract scalars ("YYYY-MM-DD", "HH:mm", enum names) and RequestParsers below
// converts them to domain scalars, rejecting anything malformed with InvalidRequestException -> HTTP
// 400 + the same error envelope the business rules use.
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using WorkTime.Domain;

namespace WorkTime.Api;

// Syntax-only patterns from api-contracts.md §3. They check the shape a client sends, never a rule.
public static class RequestPatterns
{
    public const string DatePattern = @"^\d{4}-\d{2}-\d{2}$";

    public const string TimeOfDayPattern = @"^([01]\d|2[0-3]):[0-5]\d$";

    public const string DateErrorMessage = "[RU] Field must be a date in YYYY-MM-DD form.";

    public const string TimeOfDayErrorMessage = "[RU] Field must be a time of day in HH:mm form.";
}

public class LoginRequest
{
    [Required]
    public string? login { get; set; }

    [Required]
    public string? password { get; set; }
}

public class StartWorkDayRequest
{
    [Required]
    [RegularExpression(RequestPatterns.DatePattern, ErrorMessage = RequestPatterns.DateErrorMessage)]
    public string? date { get; set; }

    [Required]
    [RegularExpression(RequestPatterns.TimeOfDayPattern, ErrorMessage = RequestPatterns.TimeOfDayErrorMessage)]
    public string? startTime { get; set; }
}

public class FinishWorkDayRequest
{
    [Required]
    [RegularExpression(RequestPatterns.TimeOfDayPattern, ErrorMessage = RequestPatterns.TimeOfDayErrorMessage)]
    public string? endTime { get; set; }
}

public class StartBreakRequest
{
    [Required]
    [RegularExpression(RequestPatterns.TimeOfDayPattern, ErrorMessage = RequestPatterns.TimeOfDayErrorMessage)]
    public string? startTime { get; set; }
}

public class FinishBreakRequest
{
    [Required]
    [RegularExpression(RequestPatterns.TimeOfDayPattern, ErrorMessage = RequestPatterns.TimeOfDayErrorMessage)]
    public string? endTime { get; set; }
}

public class CreateReportRequest
{
    // BR12 makes the text mandatory, but no BR fixes a length, so no StringLength is invented here.
    [Required]
    public string? text { get; set; }
}

public class SetAbsenceRequest
{
    [Required]
    [RegularExpression(RequestPatterns.DatePattern, ErrorMessage = RequestPatterns.DateErrorMessage)]
    public string? date { get; set; }

    // The allowed tokens come from AbsenceReason itself (see TryParseAbsenceReason), so a new
    // enumerator cannot be rejected by a hard-coded pattern here.
    [Required]
    public string? reason { get; set; }
}

// Wire scalar -> domain scalar, with the 400 envelope for anything unusable. Nothing here consults a
// repository: this is request syntax, not a business rule.
public class RequestParsers
{
    private const string DateFormat = "yyyy-MM-dd";

    // "YYYY-MM-DD" -> a date whose time part is zero (the domain stores dates that way).
    public static DateTime TryParseDateText(string? text, string fieldName)
    {
        TryRequireText(text, fieldName);

        if (!DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidRequestException(
                "DATE_INVALID",
                "[RU] Field '" + fieldName + "' must be a date in YYYY-MM-DD form.");
        }

        return date.Date;
    }

    // "HH:mm" -> a time of day. Seconds are not on the wire, so they are never produced.
    public static TimeSpan TryParseTimeOfDayText(string? text, string fieldName)
    {
        TryRequireText(text, fieldName);

        var parts = text!.Split(':');
        if (parts.Length != 2)
        {
            throw new InvalidRequestException(
                "TIME_INVALID",
                "[RU] Field '" + fieldName + "' must be a time of day in HH:mm form.");
        }

        var hours = TryParseSmallInteger(parts[0]);
        var minutes = TryParseSmallInteger(parts[1]);

        if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59)
        {
            throw new InvalidRequestException(
                "TIME_INVALID",
                "[RU] Field '" + fieldName + "' must be a time of day in HH:mm form.");
        }

        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
    }

    // Enum name exactly as in Enums.cs (BR: never send the ordinal). The token list comes from the
    // enum itself, so adding an enumerator cannot silently invalidate a controller.
    public static TEnum TryParseEnum<TEnum>(string? text, string fieldName, string code)
        where TEnum : struct, Enum
    {
        TryRequireText(text, fieldName);

        var names = Enum.GetNames<TEnum>();
        foreach (var name in names)
        {
            if (string.Equals(name, text, StringComparison.Ordinal))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        throw new InvalidRequestException(
            code,
            "[RU] Field '" + fieldName + "' must be one of: " + string.Join(", ", names) + ".");
    }

    public static AbsenceReason TryParseAbsenceReason(string? text)
    {
        return TryParseEnum<AbsenceReason>(text, "reason", "REASON_INVALID");
    }

    // A text field the endpoint needs (login, report text is allowed to be empty only where the
    // contract says so).
    public static string TryRequireText(string? text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidRequestException(
                "FIELD_MISSING",
                "[RU] Field '" + fieldName + "' is required.");
        }

        return text!;
    }

    private static int TryParseSmallInteger(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
    }
}
