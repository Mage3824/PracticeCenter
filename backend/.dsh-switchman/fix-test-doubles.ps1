$ErrorActionPreference = 'Stop'
$enc = New-Object System.Text.UTF8Encoding($false)

$users = 'tests/WorkTime.Tests/InMemoryUserRepository.cs'
$c = [System.IO.File]::ReadAllText($users).Replace("`r`n", "`n")
$broken = '    // Every account, deactivated included (mirrors the production repository).    public Task SaveAsync(User user, CancellationToken cancellationToken)'
$fixed = @'
    // Every account, deactivated included (mirrors the production repository).
    public Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<User>>(new List<User>(_users.Values));
    }

    public Task SaveAsync(User user, CancellationToken cancellationToken)
'@
if (-not $c.Contains($broken)) { throw 'user anchor missing' }
[System.IO.File]::WriteAllText($users, $c.Replace($broken, $fixed.TrimEnd()), $enc)

$departments = 'tests/WorkTime.Tests/InMemoryDepartmentRepository.cs'
$c2 = [System.IO.File]::ReadAllText($departments).Replace("`r`n", "`n")
$broken2 = '    // Every department, top level included (mirrors the production repository).    public Task SaveAsync(Department department, CancellationToken cancellationToken)'
$fixed2 = @'
    // Every department, top level included (mirrors the production repository).
    public Task<IEnumerable<Department>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<Department>>(new List<Department>(_departments.Values));
    }

    public Task SaveAsync(Department department, CancellationToken cancellationToken)
'@
if (-not $c2.Contains($broken2)) { throw 'department anchor missing' }
[System.IO.File]::WriteAllText($departments, $c2.Replace($broken2, $fixed2.TrimEnd()), $enc)

Write-Output 'test doubles repaired'
