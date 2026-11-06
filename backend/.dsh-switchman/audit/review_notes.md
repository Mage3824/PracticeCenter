# Протокол независимой проверки FULL_AUDIT.md

**Дата:** 2026-11-04. **Режим:** исходники не изменялись; `dotnet build`/`test` не запускались; правки только в `FULL_AUDIT.md` и в этом файле.

## Critical: сверено по строкам кода (7 из 7)

| slug | вердикт | доказательства |
|---|---|---|
| fake-repos-shipped-as-production-storage | OK | `src/WorkTime.Api/Program.cs:53-61` — ровно 9 × `AddSingleton<…, InMemory…Repository>` |
| jwt-dev-key-in-repo-accepted-in-production | FIXED (цитата) | `appsettings.json:3` — dev-ключ; `JwtSettings.cs:10,46,52,63,83`; derivation: `TokenService.cs:33-34` (подпись), `TokenService.cs:77-78` (валидация); класс `JwtBearerSetup` объявлен в `TokenService.cs:68`; отдельного файла `JwtBearerSetup.cs` нет (glob `**/*Jwt*` → только `JwtSettings.cs`) |
| singleton-repos-vs-scoped-dbcontext | OK | `Program.cs:53-61` singleton; `:64-68` scoped сервисы; `:71` `TokenService`; `:76` `RequestContextProvider`; сид резолвит репозитории из root provider — `Program.cs:174-175` под guard `:171` |
| sync-over-async-under-global-locks | RELABELLED | совпадений `GetAwaiter().GetResult()` **18**, не 17; под замками 16 (`WorkTimeService.cs:120,125,160,163,210` под `lock :115,:154,:194`; `BreakService.cs:132,136,146` под `:128`; `AbsenceService.cs:60,69,84` под `:56`; `AuthService.cs:81,139` под `:76,:103`; `UsersController.cs:177,205,233` под `:157,:201,:228`), **вне замка** 2: `ReportService.cs:271`, `UsersController.cs:294` |
| monthly-report-n-plus-one | OK | `ReportService.cs:214` (дни на всю систему), `:224`/`:249` вложенные циклы, `:271` await на каждую ячейку, `:316-323` один репозиторный вызов + цикл, `:354` линейный скан отсутствий |
| break-finish-no-owner-check | OK | `BreaksController.cs:58` — сигнатура `(Guid breakId, FinishBreakRequest)`; `:69` — `FinishBreakAsync(breakId, endTime, ct)`; owner/role нигде не сверяются |
| no-containerization-no-healthchecks | RELABELLED (Топ-10) | glob `**/{Dockerfile*,docker-compose*,.dockerignore}` по `E:\AItest\progcentr` → 0; запись 8.1 помечена Critical, а в Топ-10 стоял High — исправлено |

## Выборочная сверка High (подтверждено)

- 2.4: `AuthService.cs:107` гейт `lockRulesApply`, ветки `:109/:119/:127`, константы `:22`, `:24`.
- 4.5: `AuthService.cs:103-105` read-modify-write под `lock`, сброс счётчика на успехе `:78`.
- 2.10: `DevSeed.cs:22-28` (фиксированные пароли), `:64-70` (`PasswordHasher.TryHash`).
- 2.11 и порядок конвейера: `Program.cs:183-184` Swagger до `:186` `UseAuthentication`; `:189` BR32 → `:191` authorization.
- 2.2/2.9/4.3/8.2: grep `AddRateLimiter|UseHttpsRedirection|UseHsts|UseHostFiltering|AddCors|UseCors|AddHealthChecks|IHostedService|BackgroundService|Timer` по `src` → ноль совпадений.
- 2.8: `ReportService.cs:76` пропускает Manager на чужом дне (BR21 на `:78`), `:107` правка закрытого дня — только Admin; чтение чужого daily закрыто корректно (`:145`).
- 3.3/3.7: `grep "^lock"` по `src` даёт ровно `WorkTimeService.cs:115,154,194`, `AuthService.cs:76,103`, `BreakService.cs:128`, `AbsenceService.cs:56`, `UsersController.cs:157,201,228`, `Middleware.cs:187`.
- 3.4: grep `ThrowIfCancellationRequested` по репо → ноль.
- 6.3/6.5: `Entities.cs:32-34`, `:48-50`, `:59-60`, `:107` — `DateTime`/`TimeSpan`; `RowVersion` grep → ноль.
- 6.6: `WorkTimeService.cs:175` `DateTime.Now` + `AuditLog` `:177`; `ReportService.cs:98`, `:116`; `TokenService.cs:30`, `Middleware.cs:270,287,296`, `AuthController.cs:71` — `DateTime.UtcNow`.
- 7.4: grep `Task.WhenAll` по `tests` → ноль.
- 7.5: `E:\AItest\progcentr\roadmap.md:5`, `:28` — заявлено «55 тестов (25 unit + 30 integration)».
- 8.1/8.6: glob `**/{.github/**,*.yml,Dockerfile*}` по `E:\AItest\progcentr` → 0.

## Правки в FULL_AUDIT.md

1. **Топ-10:** `no-containerization-no-healthchecks` High → **Critical** (согласовано с записью 8.1; счёт Critical=7 остался верным).
2. **2.1:** цитата `JwtBearerSetup.cs:77-78` → `TokenService.cs:77-78`, плюс указание, что класс `JwtBearerSetup` в `TokenService.cs:68` (файла `JwtBearerSetup.cs` в репозитории нет).
3. **3.2:** «17 совпадений, все внутри `lock`» → «18 совпадений: 16 под замками, 2 вне (`ReportService.cs:271`, `UsersController.cs:294`)».
4. **3.3:** BR20-скан `UsersController.cs:286-294` выполняется **вне** замка (было «под тем же замком»).
5. **6.2** и пункт 5 «Что обязательно исправить»: TOCTO → TOCTOU.
6. **Пути к схеме:** `../project/database/schema.sql` → `../database/schema.sql` (файл — `E:\AItest\progcentr\project\database\schema.sql`; прежний путь от `backend` разрешался в несуществующий `project\project\database`). Заменено везде, где встречалось.
7. **6.6:** удалён ориентир «Pomelo commit 6b1e9ca…» — HTTP 404 (страница не существует).
8. **Новая запись 8.8 «CORS не настроен вообще» (Medium):** подтема ТЗ (CORS) в отчёте отсутствовала; был только «отложенный» пункт со ссылкой на несуществующий §8.8 и утверждением про `AllowAnyOrigin`. Счёт находок: Medium 12 → 13, всего 65 → 66.
9. **Орфография/склейки:** `17support-кодов` → `17 реализаций support-кода`; `правка одного忘记 обновить второй` → `правка одного без второго`; `расхождениеDomain ↔ API` → `расхождение Domain ↔ API`; `недостиима` → `недостижима` (+ закрытая кавычка «обязательное логирование»); `` `:171`).EF Core `` → `` `:171`). EF Core ``.
10. **Ссылка CORS:** в новой записи ориентир сознательно не добавлен (внешнюю страницу не сверял, выдуманный URL не вводится).

## Ссылки («Ориентир:»)

- Проверены, 200 OK: `raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/performance/rate-limit.md`; `raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/test/integration-tests.md`; `owasp.github.io/www-project-web-security-testing-guide/latest/4-Web_Application_Security_Testing/04-Authentication/03-Weak_Lock_Out_Mechanism` (canonical — `owasp.org/…` тот же путь).
- Удалена (404): `github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/commit/6b1e9ca2941fb9c002374e71214c6810c3cf6763`.
- Не сверялись (оставлены): ссылки Microsoft Docs (`ef/core/testing`, `ef/core/saving/concurrency`, `host-and-deploy/health-checks`, `security/enforcing-ssl`), OWASP CheatSheetSeries/ASVS/API Security, RFC 7518 §3.2, Pomelo wiki Configuration-Options.
- Внутренние пути отсчёта (`backend`): `api-contracts.md`, `../docs/business_rules.md`, `../README.md`, `E:\AItest\progcentr\roadmap.md`, `../database/schema.sql` — все существуют (glob).

## Остаточные риски

- Нумерация запис идёт «дырками» (3.8–3.11, 4.10–4.15, 6.8, 6.10, 8.5, 8.7): следы удалённых лановых находок; противоречий в тексте нет, но номера не последовательны.
- 7.5 (51 тестов против 55 в roadmap) и 5.1 (17 реализаций `GetAllByIdsAsync`, 0 вызовов) сверены частично: полные списки grep обрезались; формальных расхождений не найдено.
- 2.3 («отказ возвращается как данные, HTTP 200») подтверждён по `AuthController.cs:40-43`, точные строки `:55-57` не перечитывались.
- Утверждение «Kestrel по умолчанию 30 MB» (2.2, 8.9) — общеизвестный дефолт, в коде не проверяется; оставлено.
- Нумерация БП/BR в отчёте смешана (`БП…` и `BR…`), смысловых противоречий не выявлено.
