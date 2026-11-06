# -*- coding: utf-8 -*-
# CS1996 финал: dayBreaks остался вне lock (ожидание разрешено), чиним только await внутри lock.
import sys

ROOT = r"E:\AItest\progcentr\project\backend"

def patch(rel, pairs):
    path = ROOT + "\\src\\" + rel
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        text = f.read()
    nl = "\r\n" if "\r\n" in text else "\n"
    work = text.replace("\r\n", "\n")
    for old, new, n in pairs:
        c = work.count(old)
        if c != n:
            print("FAIL %s: count=%d expected=%d for %r" % (rel, c, n, old[:44]))
            sys.exit(1)
        work = work.replace(old, new)
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(work.replace("\n", nl))
    print("OK %s (%d)" % (rel, len(pairs)))

patch("WorkTime.Application\\Services\\WorkTimeService.cs", [
    ("            var workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);",
     "            var workedTime = CalculateWorkedTimeAsync(workDay, cancellationToken).GetAwaiter().GetResult();", 1),
    ("            workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);",
     "            workedTime = CalculateWorkedTimeAsync(workDay, cancellationToken).GetAwaiter().GetResult();", 1),
    ("            await _workDays.SaveAsync(workDay, cancellationToken);",
     "            _workDays.SaveAsync(workDay, cancellationToken).GetAwaiter().GetResult();", 2),
    ("                await _breaks.SaveAsync(workBreak, cancellationToken);",
     "                _breaks.SaveAsync(workBreak, cancellationToken).GetAwaiter().GetResult();", 1),
])

patch("WorkTime.Application\\Services\\BreakService.cs", [
    ("            var otherBreaks = await SumCountedBreakTimeAsync(workBreak.WorkDayId, workBreak.Id, cancellationToken);",
     "            var otherBreaks = SumCountedBreakTimeAsync(workBreak.WorkDayId, workBreak.Id, cancellationToken).GetAwaiter().GetResult();", 1),
    ("                await _workDays.SaveAsync(workDay, cancellationToken);",
     "                _workDays.SaveAsync(workDay, cancellationToken).GetAwaiter().GetResult();", 1),
    ("            await _breaks.SaveAsync(workBreak, cancellationToken);",
     "            _breaks.SaveAsync(workBreak, cancellationToken).GetAwaiter().GetResult();", 1),
])

print("DONE")
