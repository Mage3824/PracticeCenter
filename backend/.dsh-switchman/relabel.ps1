# Переметка приоритетов в FULL_AUDIT.md (только метки и сводные списки; тексты записей не меняются)
$p = 'E:\AItest\progcentr\project\backend\FULL_AUDIT.md'
$bak = 'E:\AItest\progcentr\project\backend\.dsh-switchman\FULL_AUDIT.md.bak'
$t = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
if (-not (Test-Path $bak)) { [System.IO.File]::WriteAllText($bak, $t, (New-Object System.Text.UTF8Encoding($false))) }
$nl = if ($t.Contains("`r`n")) { "`r`n" } else { "`n" }

$reps = [ordered]@{
  'правила агрегатов в контроллерах — **Приоритет: High**'            = 'правила агрегатов в контроллерах — **Приоритет: Medium**'
  'DTO-слой мёртвый — **Приоритет: High**'                            = 'DTO-слой мёртвый — **Приоритет: Medium**'
  'timing oracle на `/login` — **Приоритет: High**'                   = 'timing oracle на `/login` — **Приоритет: Medium**'
  'фиксированными паролями, включая Admin — **Приоритет: High**'      = 'фиксированными паролями, включая Admin — **Приоритет: Medium**'
  'Swagger UI и schema анонимны и в Production — **Приоритет: High**' = 'Swagger UI и schema анонимны и в Production — **Приоритет: Medium**'
  'N+1 с await в тройном вложенном цикле — **Приоритет: Critical**'   = 'N+1 с await в тройном вложенном цикле — **Приоритет: High**'
  'Sync-over-async под глобальными мьютексами — **Приоритет: Critical**' = 'Sync-over-async под глобальными мьютексами — **Приоритет: High**'
  'ни в одной реализации — **Приоритет: High**'                       = 'ни в одной реализации — **Приоритет: Medium**'
  'Audit log без bounding и без страниц — **Приоритет: High**'        = 'Audit log без bounding и без страниц — **Приоритет: Medium**'
  'без проверки владельца и роли — **Приоритет: Critical**'           = 'без проверки владельца и роли — **Приоритет: High**'
  'не формируется автоматически при завершении дня — **Приоритет: High**' = 'не формируется автоматически при завершении дня — **Приоритет: Medium**'
  '0 вызовов — **Приоритет: High**'                                   = '0 вызовов — **Приоритет: Low**'
  'во всех 8 контроллерах — **Приоритет: High**'                      = 'во всех 8 контроллерах — **Приоритет: Medium**'
  'два идентичных тела — **Приоритет: High**'                         = 'два идентичных тела — **Приоритет: Medium**'
  'локально в контроллере — **Приоритет: High**'                      = 'локально в контроллере — **Приоритет: Medium**'
  '`DateTime` без tz — **Приоритет: High**'                           = '`DateTime` без tz — **Приоритет: Medium**'
  'есть null-forgiving — **Приоритет: High**'                         = 'есть null-forgiving — **Приоритет: Medium**'
  'боевые репозитории не тестируются — **Приоритет: High**'           = 'боевые репозитории не тестируются — **Приоритет: Medium**'
  'development-ключом из репозитория — **Приоритет: High**'           = 'development-ключом из репозитория — **Приоритет: Low**'
  'auto-finish, 501 — **Приоритет: High**'                            = 'auto-finish, 501 — **Приоритет: Medium**'
  'Нет конкурентных тестов — **Приоритет: High**'                     = 'Нет конкурентных тестов — **Приоритет: Medium**'
  'Карта покрытия BR: незакрытые правила — **Приоритет: High**'      = 'Карта покрытия BR: незакрытые правила — **Приоритет: Medium**'
  'ни `.dockerignore` — **Приоритет: Critical**'                      = 'ни `.dockerignore` — **Приоритет: Medium**'
  'Health-checks отсутствуют — **Приоритет: High**'                   = 'Health-checks отсутствуют — **Приоритет: Medium**'
  'Часовой пояс не конфигурируется для BR3/BR33 — **Приоритет: High**' = 'Часовой пояс не конфигурируется для BR3/BR33 — **Приоритет: Medium**'
  'без correlation id и метрик — **Приоритет: High**'                 = 'без correlation id и метрик — **Приоритет: Medium**'
  'Нет CI/CD и анализаторов — **Приоритет: High**'                    = 'Нет CI/CD и анализаторов — **Приоритет: Medium**'
  '| sync-over-async-under-global-locks | Critical |'                 = '| sync-over-async-under-global-locks | High |'
  '| monthly-report-n-plus-one | Critical |'                          = '| monthly-report-n-plus-one | High |'
  '| break-finish-no-owner-check | Critical |'                        = '| break-finish-no-owner-check | High |'
  '| no-containerization-no-healthchecks | Critical |'                = '| no-containerization-no-healthchecks | Medium |'
  'как прод (1.1, 6.11).'                                             = 'как прод (1.1).'
  'замками и пробросить `CancellationToken` (3.2, 3.4, 3.3).'         = 'замками (3.2, 3.3).'
  '11. Rate limiting на `/api/auth/login` + лимит тела 64 KB и единый текст отказа с выравниванием времени (2.2, 2.3, 8.9).' = '11. Rate limiting на `/api/auth/login` (2.2).'
  '13. `UseHttpsRedirection`/`UseHsts` + `UseHostFiltering`, Swagger только вне Production (2.9, 2.11).' = '12. `UseHttpsRedirection`/`UseHsts` + `UseHostFiltering` (2.9).'
  '16. BR35/BR36 для всех ролей: убрать `lockRulesApply`, править контракт и инвертировать тест (2.4, 7.5).' = '13. BR35/BR36 для всех ролей: убрать `lockRulesApply`, править контракт и инвертировать тест (2.4).'
  'EF (1.4, 3.3);'                                                    = 'EF (1.4);'
  'лимит тела 64 KB.'                                                = 'лимит тела 64 KB.'
}

$miss = @()
foreach ($k in $reps.Keys) {
  if (-not $t.Contains($k)) { $miss += $k; continue }
  $t = $t.Replace($k, $reps[$k])
}

# удаление из обязательного списка того, что стало Medium/Low
$del = @(
  '14. `AddHealthChecks` + `/health/live`, `/health/ready` и они в контрактах (8.2).' + $nl,
  '15. Dockerfile + docker-compose с MySQL и `stop_grace_period` (8.1).' + $nl,
  '17. Пагинация и упорядочение в list-эндпоинтах и истории аудита (3.12, 3.5).' + $nl,
  '18. CI: restore → format → build → test с coverage-gate и анализаторы в csproj (8.6, 5.6).' + $nl
)
foreach ($d in $del) { if (-not $t.Contains($d)) { $miss += 'DEL-MISS' } else { $t = $t.Replace($d, '') } }

# оставшиеся High добавляются в обязательный список одним пунктом 14
$tail13 = '13. BR35/BR36 для всех ролей: убрать `lockRulesApply`, править контракт и инвертировать тест (2.4).'
$add14  = $tail13 + $nl + '14. Остальные High до Этапа 3: BR32 resurrection после prune (2.6), серверная проверка капчи BR36 (2.5), owner-gate чужого отчёта + логирование БП23 (2.8), BR3 авто-закрытие 23:59:59 (4.3), БП12/БП23 правки часов и перерывов (4.7), блокировка редактирования закрытого периода (4.8), валидация отрицательной длительности (4.9), гонки plain `Dictionary` (3.6), in-memory состояние на 2+ инстансах (3.7), O(n)-сканы и N+1 на EF (6.7).'
if ($t.Contains($tail13)) { $t = $t.Replace($tail13, $add14) } else { $miss += 'ADD14-MISS' }

# новые отложенные пункты (то, что стало Medium/Low)
$devops = 'README + `api.env.example` + `UserSecretsId`, `stop_grace_period` в compose, лимит тела 64 KB.'
$extra  = $devops + $nl + '- Контейнеризация и наблюдаемость Этапа 3/4: Dockerfile + docker-compose с MySQL (8.1), `AddHealthChecks` + `/health/live` и `/health/ready` (8.2), `TZ`/NTP и `UseUserTimeZone` (8.3), структурированное логирование с correlation id и метриками (8.4), CI/CD pipeline с coverage-gate и анализаторы + NRT-warnings as errors (8.6, 5.6).' + $nl + '- Негативные и интеграционные тесты: единый текст отказа и выравнивание времени на `/login` (2.3), тесты боевых репозиториев и конкурентные тесты (7.1, 7.3, 7.4), карта покрытия BR (7.5), переопределение ключа и `UseEnvironment` в `WebApplicationFactory` (7.2).' + $nl + '- Пагинация, упорядочение и bounding аудита по БП29 (3.5, 3.12), проброс `CancellationToken` в EF (3.4), use-case сервисы в Application (1.2) и выбор DTO/wire-контракта (1.3), auto-генерация отчёта по БП14 (4.6).'
if ($t.Contains($devops)) { $t = $t.Replace($devops, $extra) } else { $miss += 'EXTRA-MISS' }

# строка переметки в конце файла
$stamp = '**Переметка:** 27 записей скорректировано (27 понижен, 0 повышен), counts: Critical — 3, High — 26, Medium — 35, Low — 2 (всего 66).'
if (-not $t.Contains('**Переметка:**')) { $t = $t.TrimEnd() + $nl + $stamp + $nl }

[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($false)))
$hdr = [regex]::Matches($t, '^### [0-9]+\.[0-9]+ .* — \*\*Приоритет: (\w+)\*\*', [System.Text.RegularExpressions.RegexOptions]::Multiline)
$c = @{}
foreach ($m in $hdr) { $c[$m.Groups[1].Value] = 1 + [int]$c[$m.Groups[1].Value] }
"headers=$($hdr.Count) C=$($c['Critical']) H=$($c['High']) M=$($c['Medium']) L=$($c['Low'])"
"miss=$($miss.Count)"
if ($miss.Count) { $miss | ForEach-Object { "MISS: $_" } }
