# Smoke-проверка исправленных эндпоинтов (запуск API на 127.0.0.1:5123).
$base='http://127.0.0.1:5123'
$dll='src\WorkTime.Api\bin\Debug\net8.0\WorkTime.Api.dll'
if (-not (Test-Path $dll)) { Write-Output 'APP DLL MISSING'; exit 2 }
$env:ASPNETCORE_ENVIRONMENT='Development'
$p = Start-Process dotnet -ArgumentList 'run','--project','src/WorkTime.Api','--no-build','--urls',$base -WorkingDirectory (Get-Location) -PassThru
$ready=$false
for($i=0; $i -lt 25; $i++){
  try { $r=Invoke-WebRequest "$base/swagger/v1/swagger.json" -TimeoutSec 3 -SkipHttpErrorCheck; if($r.StatusCode -eq 200){$ready=$true; break} } catch {}
  Start-Sleep 2
}
if(-not $ready){ Write-Output 'API DID NOT START'; Stop-Process -Id $p.Id -Force; exit 3 }
function J($o){ $o | ConvertTo-Json -Compress }
function Login($l,$pw){
  $r=Invoke-WebRequest "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body (J @{login=$l;password=$pw}) -SkipHttpErrorCheck
  $j=$r.Content|ConvertFrom-Json
  return @{status=$r.StatusCode; token=$j.accessToken; code=$j.code}
}
$emp=Login 'employee' 'employee-password-1'
$adm=Login 'admin' 'admin-password-1'
Write-Output ("login employee => {0} token={1}" -f $emp.status, ($emp.token -ne $null))
Write-Output ("login admin => {0} token={1}" -f $adm.status, ($adm.token -ne $null))
$rb=Invoke-WebRequest "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body (J @{login='employee';password='wrong'}) -SkipHttpErrorCheck
$j=($rb.Content|ConvertFrom-Json)
Write-Output ("login wrong pw => {0} isSuccess={1} code={2}" -f $rb.StatusCode,$j.isSuccess,$j.code)
$uh=@{Authorization="Bearer $($adm.token)"}
$users=Invoke-WebRequest "$base/api/users" -Headers $uh -SkipHttpErrorCheck
$uj=($users.Content|ConvertFrom-Json)
$empId=($uj|Where-Object{$_.login -eq 'employee'}).id
$admId=($uj|Where-Object{$_.login -eq 'admin'}).id
Write-Output ("GET /api/users(admin) => {0} empId={1}" -f $users.StatusCode,$empId)
$eh=@{Authorization="Bearer $($emp.token)"}
$cur=Invoke-WebRequest "$base/api/work-days/current" -Headers $eh -SkipHttpErrorCheck
Write-Output ("GET work-days/current(emp) => {0} body='{1}'" -f $cur.StatusCode,$cur.Content)
$curA=Invoke-WebRequest "$base/api/work-days/current" -Headers $uh -SkipHttpErrorCheck
Write-Output ("GET work-days/current(admin) => {0}" -f $curA.StatusCode)
$st=Invoke-WebRequest "$base/api/work-days/start" -Method Post -Headers $eh -ContentType 'application/json' -Body (J @{date='bad';startTime='09:00'}) -SkipHttpErrorCheck
$js=($st.Content|ConvertFrom-Json)
Write-Output ("POST work-days/start(bad date) => {0} code={1} error.code={2} error.httpStatus={3}" -f $st.StatusCode,$js.code,$js.error.code,$js.error.httpStatus)
$au=Invoke-WebRequest "$base/api/audit-logs?entityId=$empId" -Headers $uh -SkipHttpErrorCheck
Write-Output ("GET /api/audit-logs(admin) => {0}" -f $au.StatusCode)
$auE=Invoke-WebRequest "$base/api/audit-logs?entityId=$empId" -Headers $eh -SkipHttpErrorCheck
Write-Output ("GET /api/audit-logs(emp) => {0}" -f $auE.StatusCode)
$ab=Invoke-WebRequest "$base/api/absences/$empId`?date=2026-11-03" -Headers $eh -SkipHttpErrorCheck
Write-Output ("GET absences/own(emp) => {0} body='{1}'" -f $ab.StatusCode,$ab.Content)
$ab2=Invoke-WebRequest "$base/api/absences/$admId`?date=2026-11-03" -Headers $eh -SkipHttpErrorCheck
Write-Output ("GET absences/other(emp) => {0}" -f $ab2.StatusCode)
$ab3=Invoke-WebRequest "$base/api/absences/$empId`?date=2026-11-03" -Headers $uh -SkipHttpErrorCheck
Write-Output ("GET absences/own(admin) => {0}" -f $ab3.StatusCode)
$rp=Invoke-WebRequest "$base/api/reports/daily/00000000-0000-0000-0000-000000000001" -Headers $eh -SkipHttpErrorCheck
Write-Output ("GET reports/daily(missing) => {0} code={1}" -f $rp.StatusCode,(($rp.Content|ConvertFrom-Json).code))
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Get-NetTCPConnection -LocalPort 5123 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
Write-Output 'SMOKE DONE'
