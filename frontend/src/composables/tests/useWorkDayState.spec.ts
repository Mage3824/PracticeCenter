import { describe, it, expect, vi } from 'vitest';
import { useWorkDayState } from '../useWorkDayState';

describe('useWorkDayState Machine', () => {
  const fakeEmployeeId = '11111111-1111-1111-1111-111111111111';

  it('должен блокировать старт смены, если есть статус отсутствия (БП27)', async () => {
    // Эмулируем ответ от API: canStart = false
    const mockApiClient = {
      get: vi.fn().mockResolvedValue({ data: { canStart: false } }),
      post: vi.fn(),
    } as any;

    const { state, canStartDay, checkCanStart } = useWorkDayState(fakeEmployeeId, mockApiClient);

    await checkCanStart();

    expect(state.value).toBe('BLOCKED_BY_ABSENCE');
    expect(canStartDay.value).toBe(false);
  });

  it('должен переводить смену в состояние WORKING после успешного старта', async () => {
    const mockApiClient = {
      get: vi.fn().mockResolvedValue({ data: { canStart: true } }),
      post: vi.fn().mockResolvedValue({
        data: {
          id: 'day-1',
          userId: fakeEmployeeId,
          date: '2026-10-30',
          startTime: '09:00',
          endTime: null,
          status: 'Active',
          countedInReports: true,
          isBreakLimitExceeded: false,
        },
      }),
    } as any;

    const { state, startDay, canStartBreak, canFinishDay } = useWorkDayState(fakeEmployeeId, mockApiClient);

    await startDay('09:00');

    expect(state.value).toBe('WORKING');
    expect(canStartBreak.value).toBe(true);
    expect(canFinishDay.value).toBe(true);
  });

  it('должен переключаться в ON_BREAK при старте перерыва', async () => {
    const mockApiClient = {
      post: vi.fn().mockResolvedValue({
        data: {
          id: 'break-1',
          workDayId: 'day-1',
          startTime: '12:00',
          endTime: null,
          durationMinutes: null,
          countedInReports: true,
        },
      }),
    } as any;

    const { state, startBreak, canFinishBreak, syncExistingShift } = useWorkDayState(fakeEmployeeId, mockApiClient);

    // Устанавливаем открытую смену
    syncExistingShift({
      id: 'day-1',
      userId: fakeEmployeeId,
      date: '2026-10-30',
      startTime: '09:00',
      endTime: null,
      status: 'Active',
      countedInReports: true,
      isBreakLimitExceeded: false,
    });

    await startBreak('12:00');

    expect(state.value).toBe('ON_BREAK');
    expect(canFinishBreak.value).toBe(true);
  });
});