// JwtSettings read out of the ASP.NET Core configuration (appsettings.json section "JwtSettings").
// Stage 2 keeps the same four values the wire contract fixes: SecretKey / Issuer / Audience /
// ExpiryMinutes. HS256 signing requires a key of at least 32 bytes, so the values are validated
// before the token pipeline is built.
namespace WorkTime.Api;

public class JwtSettings
{
    // BR32: the token lifetime and the session inactivity limit are both 8 hours.
    public const int DefaultExpiryMinutes = 480;

    // Аудит 2.1: ключ подписи берётся ТОЛЬКО из конфигурации (appsettings, переменные окружения,
    // dotnet user-secrets). Дефолтный ключ из репозитория из кода удалён — код ничего не подставляет.
    public string SecretKey { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int ExpiryMinutes { get; set; } = DefaultExpiryMinutes;
}

// Binds the "JwtSettings" section of the configuration and validates it (Stage 2).
public class JwtSettingsFactory
{
    // HS256 needs a key of at least this many bytes (256 bits).
    public const int MinSecretKeyBytes = 32;

    // Точные имена из конфигурации: секция "JwtSettings", ключ "SecretKey".
    public const string SettingsSectionName = "JwtSettings";
    public const string SecretKeyConfigKey = "SecretKey";

    // Прежний dev-дефолт из репозитория. Он больше нигде не подставляется как fallback: этот
    // список только отвергает ключ (аудит 2.1), поэтому значение хранится как запрет, а не как подмена.
    public const string RejectedDevelopmentSecretKey =
        "work-time-development-signing-key-change-me-please-32+-bytes";

    public static JwtSettings TryFromConfiguration(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        var settings = configuration.GetSection(SettingsSectionName).Get<JwtSettings>();

        if (settings == null)
        {
            // Отсутствие секции — ошибка конфигурации при любом окружении: тихая подмена
            // ключа недопустима и вне Development, и в Development (аудит 2.1).
            throw new InvalidRequestException(
                "CONFIGURATION",
                MissingSigningKeyMessage());
        }

        // Молча не чиним ExpiryMinutes: невалидное значение отдаёт ValidateSettings как
        // явную ошибку конфигурации (правка аудита).
        TryValidateSigningKey(settings);
        ValidateSettings(settings);

        return settings;
    }

    // Жёсткая проверка ключа подписи (аудит 2.1). Вызывается на старте приложения, чтобы
    // пустой или прежний дефолтный ключ ронял процесс, а не проявлялся при первом выпуске токена.
    public static void TryValidateSigningKey(JwtSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SecretKey)
            || settings.SecretKey == RejectedDevelopmentSecretKey)
        {
            throw new InvalidRequestException(
                "CONFIGURATION",
                MissingSigningKeyMessage());
        }

        if (System.Text.Encoding.UTF8.GetByteCount(settings.SecretKey) < MinSecretKeyBytes)
        {
            throw new InvalidRequestException(
                "CONFIGURATION",
                "[RU] Ключ подписи JWT должен быть не короче " + MinSecretKeyBytes +
                " байт (256 бит для HS256). Задайте его в конфигурации: секция \"" +
                SettingsSectionName + "\", ключ \"" + SecretKeyConfigKey + "\" " +
                "(или переменная окружения " + SettingsSectionName + "__" + SecretKeyConfigKey + "). " +
                "Для локального запуска: dotnet user-secrets set \"" + SettingsSectionName + ":" +
                SecretKeyConfigKey + "\" \"<ключ>\".");
        }
    }

    private static string MissingSigningKeyMessage()
    {
        return "[RU] Ключ подписи JWT не задан: секция \"" + SettingsSectionName + "\" отсутствует, "
            + "ключ \"" + SecretKeyConfigKey + "\" пуст или состоит из пробелов, либо равен прежнему "
            + "дефолтному значению из репозитория. Задайте ключ через конфигурацию или переменную "
            + "окружения " + SettingsSectionName + "__" + SecretKeyConfigKey + ". "
            + "Для локального запуска: dotnet user-secrets set \"" + SettingsSectionName + ":" +
            SecretKeyConfigKey + "\" \"<ключ>\".";
    }

    private static void ValidateSettings(JwtSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience))
        {
            throw new InvalidRequestException(
                "CONFIGURATION",
                "[RU] JwtSettings must define Issuer and Audience.");
        }

        if (settings.ExpiryMinutes <= 0)
        {
            throw new InvalidRequestException(
                "CONFIGURATION",
                "[RU] JwtSettings.ExpiryMinutes must be a positive number of minutes.");
        }
    }
}
