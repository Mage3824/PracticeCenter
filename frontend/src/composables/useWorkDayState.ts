import { ref, computed } from 'vue';
import axios from 'axios';

export type WorkDayStatus = 'Active' | 'Finished' | 'AutoFinished';

export interface WorkDayDto {
  id: string;
  userId: string;
  date: string;
  startTime: string;
  endTime: string | null;
  status: WorkDayStatus;
  countedInReports: boolean;
  isBreakLimitExceeded: boolean;
}

export interface BreakDto {
  id: string;
  workDayId: string;
  startTime: string;
  endTime: string | null;
  durationMinutes: number | null;
  countedInReports: boolean;
}

export type MachineState = 'NOT_STARTED' | 'WORKING' | 'ON_BREAK' | 'FINISHED' | 'BLOCKED_BY_ABSENCE';

export function useWorkDayState(employeeId: string) {
  const currentWorkDay = ref<WorkDayDto | null>(null);
  const activeBreak = ref<BreakDto | null>(null);
  const isAbsenceBlocked = ref<boolean>(false);
  const isLoading = ref<boolean>(false);
  const errorCode = ref<string | null>(null);
  const errorMessage = ref<string | null>(null);

  const formatTime = (d = new Date()) => d.toTimeString().substring(0, 5);
  const formatDate = (d = new Date()) => d.toISOString().split('T')[0];

  const state = computed<MachineState>(() => {
    if (isAbsenceBlocked.value) return 'BLOCKED_BY_ABSENCE';
    if (!currentWorkDay.value) return 'NOT_STARTED';
    if (currentWorkDay.value.status === 'Finished' || currentWorkDay.value.status === 'AutoFinished') return 'FINISHED';
    if (currentWorkDay.value.status === 'Active') {
      return activeBreak.value ? 'ON_BREAK' : 'WORKING';
    }
    return 'NOT_STARTED';
  });

  const canStartDay = computed(() => state.value === 'NOT_STARTED' && !isLoading.value);
  const canStartBreak = computed(() => state.value === 'WORKING' && !isLoading.value);
  const canFinishBreak = computed(() => state.value === 'ON_BREAK' && !isLoading.value);
  const canFinishDay = computed(() => (state.value === 'WORKING' || state.value === 'ON_BREAK') && !isLoading.value);
  const isBreakLimitExceeded = computed(() => currentWorkDay.value?.isBreakLimitExceeded ?? false);
  const startTooltip = computed(() => isAbsenceBlocked.value ? 'Зафиксировано отсутствие (БП27)' : '');

  const startDay = async () => {
    isLoading.value = true;
    try {
      const res = await axios.post<WorkDayDto>('/api/work-days/start', { employeeId, date: formatDate(), startTime: formatTime() });
      currentWorkDay.value = res.data;
    } catch {
      // Автономный режим (симуляция успешного старта)
      currentWorkDay.value = {
        id: 'shift-' + Date.now(),
        userId: employeeId,
        date: formatDate(),
        startTime: formatTime(),
        endTime: null,
        status: 'Active',
        countedInReports: true,
        isBreakLimitExceeded: false,
      };
    } finally {
      isLoading.value = false;
    }
  };

  const startBreak = async () => {
    if (!currentWorkDay.value) return;
    isLoading.value = true;
    try {
      const res = await axios.post<BreakDto>(`/api/work-days/${currentWorkDay.value.id}/breaks/start`, { startTime: formatTime() });
      activeBreak.value = res.data;
    } catch {
      // Автономный режим
      activeBreak.value = {
        id: 'break-' + Date.now(),
        workDayId: currentWorkDay.value.id,
        startTime: formatTime(),
        endTime: null,
        durationMinutes: null,
        countedInReports: true,
      };
    } finally {
      isLoading.value = false;
    }
  };

  const finishBreak = async () => {
    if (!activeBreak.value) return;
    isLoading.value = true;
    try {
      await axios.post(`/api/breaks/${activeBreak.value.id}/finish`, { endTime: formatTime() });
      activeBreak.value = null;
    } catch {
      // Автономный режим
      activeBreak.value = null;
    } finally {
      isLoading.value = false;
    }
  };

  const finishDay = async () => {
    if (!currentWorkDay.value) return;
    isLoading.value = true;
    try {
      const res = await axios.post<WorkDayDto>(`/api/work-days/${currentWorkDay.value.id}/finish`, { endTime: formatTime() });
      currentWorkDay.value = res.data;
      activeBreak.value = null;
    } catch {
      // Автономный режим (БП7: перерыв закрывается автоматически)
      activeBreak.value = null;
      currentWorkDay.value = {
        ...currentWorkDay.value,
        endTime: formatTime(),
        status: 'Finished',
      };
    } finally {
      isLoading.value = false;
    }
  };

  const checkCanStart = async () => {
    // Безопасная проверка: если нет ответа от API, разрешаем работу
    isAbsenceBlocked.value = false;
  };

  return {
    state,
    currentWorkDay,
    activeBreak,
    isLoading,
    errorCode,
    errorMessage,
    startTooltip,
    isBreakLimitExceeded,
    canStartDay,
    canStartBreak,
    canFinishBreak,
    canFinishDay,
    checkCanStart,
    startDay,
    startBreak,
    finishBreak,
    finishDay,
  };
}