<template>
  <div class="app-container">
    <header class="header">
      <h2>Система учёта рабочего времени</h2>
      <span class="user-info">Практикант 3 (Тестовый стенд)</span>
    </header>

    <main class="main-card">
      <!-- Переключатель режима для автономной проверки без API -->
      <div class="mode-bar">
        <label class="toggle-label">
          <input type="checkbox" v-model="isDemoMode" @change="onToggleDemo" />
          <span>Режим офлайн-симуляции (без запущенного бэкенда)</span>
        </label>
        <span class="badge" :class="isDemoMode ? 'badge-warning' : 'badge-info'">
          {{ isDemoMode ? 'Офлайн (эмуляция)' : 'Подключение к API' }}
        </span>
      </div>

      <!-- Сообщения об ошибках бизнес-правил (409 Conflict) -->
      <div v-if="errorMessage" class="alert alert-error">
        <strong>[{{ errorCode || 'ОШИБКА' }}]</strong> {{ errorMessage }}
      </div>

      <!-- Предупреждение о превышении 4 часов перерывов (БП9) -->
      <div v-if="isBreakLimitExceeded" class="alert alert-warning">
        Внимание: Превышен суточный лимит перерывов (4 часа). Уведомление направлено руководителю.
      </div>

      <!-- Индикатор текущего состояния автомата смены -->
      <section class="status-panel">
        <div class="status-title">Текущее состояние смены:</div>
        <div class="status-value" :class="statusClass">
          {{ stateLabels[state] || state }}
        </div>
      </section>

      <!-- Панель кнопок управления сменой -->
      <section class="actions-panel">
        <!-- 1. Начать работу (БП1, БП27) -->
        <div class="button-wrapper" :title="startTooltip">
          <button
            class="btn btn-start"
            :disabled="!canStartDay || isLoading"
            @click="handleStartDay"
          >
            Начать работу
          </button>
        </div>

        <!-- 2. На перерыв (БП6, БП10) -->
        <button
          v-if="state === 'WORKING' || state === 'NOT_STARTED'"
          class="btn btn-break-start"
          :disabled="!canStartBreak || isLoading"
          @click="handleStartBreak"
        >
          На перерыв
        </button>

        <!-- 3. Завершить перерыв (БП8, БП10) -->
        <button
          v-if="state === 'ON_BREAK'"
          class="btn btn-break-finish"
          :disabled="!canFinishBreak || isLoading"
          @click="handleFinishBreak"
        >
          Завершить перерыв
        </button>

        <!-- 4. Закончить работу (БП2, автозакрытие по БП7) -->
        <button
          class="btn btn-finish"
          :disabled="!canFinishDay || isLoading"
          @click="handleFinishDay"
        >
          Закончить смену
        </button>
      </section>

      <!-- Информационный блок текущей сессии -->
      <section class="details-grid">
        <div class="info-box">
          <span class="info-label">Дата смены</span>
          <span class="info-data">{{ currentWorkDay?.date || '—' }}</span>
        </div>
        <div class="info-box">
          <span class="info-label">Начало работы</span>
          <span class="info-data">{{ currentWorkDay?.startTime || '—' }}</span>
        </div>
        <div class="info-box">
          <span class="info-label">Текущий перерыв</span>
          <span class="info-data">{{ activeBreak?.startTime ? `с ${activeBreak.startTime}` : 'нет' }}</span>
        </div>
        <div class="info-box">
          <span class="info-label">Конец работы</span>
          <span class="info-data">{{ currentWorkDay?.endTime || '—' }}</span>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useWorkDayState } from './composables/useWorkDayState';

// Тестовый ID сотрудника (на этапе авторизации берётся из JWT)
const currentUserId = '5a1c9b0e-2f31-4a6d-9b7c-0d2e4f60a8b1';

// Подключение модуля автомата состояний
const {
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
  syncExistingShift,
} = useWorkDayState(currentUserId);

// Режим симуляции (позволяет тестировать UI даже когда бэкенд выключен)
const isDemoMode = ref(false);

const stateLabels: Record<string, string> = {
  NOT_STARTED: 'Смена не начата',
  WORKING: 'В работе',
  ON_BREAK: 'На перерыве',
  FINISHED: 'Смена завершена',
  BLOCKED_BY_ABSENCE: 'Заблокировано (Больничный/Отпуск)',
  CHECKING: 'Проверка прав...',
};

const statusClass = computed(() => {
  switch (state.value) {
    case 'WORKING': return 'status-working';
    case 'ON_BREAK': return 'status-break';
    case 'FINISHED': return 'status-finished';
    case 'BLOCKED_BY_ABSENCE': return 'status-blocked';
    default: return 'status-default';
  }
});

const getNowTime = () => new Date().toTimeString().substring(0, 5);
const getNowDate = () => new Date().toISOString().split('T')[0];

const onToggleDemo = () => {
  syncExistingShift(null, null);
};

const handleStartDay = async () => {
  if (isDemoMode.value) {
    syncExistingShift({
      id: 'demo-day-001',
      userId: currentUserId,
      date: getNowDate(),
      startTime: getNowTime(),
      endTime: null,
      status: 'Active',
      countedInReports: true,
      isBreakLimitExceeded: false,
    }, null);
    return;
  }
  await startDay();
};

const handleStartBreak = async () => {
  if (isDemoMode.value && currentWorkDay.value) {
    syncExistingShift(currentWorkDay.value, {
      id: 'demo-break-001',
      workDayId: currentWorkDay.value.id,
      startTime: getNowTime(),
      endTime: null,
      durationMinutes: null,
      countedInReports: true,
    });
    return;
  }
  await startBreak();
};

const handleFinishBreak = async () => {
  if (isDemoMode.value && currentWorkDay.value) {
    syncExistingShift(currentWorkDay.value, null);
    return;
  }
  await finishBreak();
};

const handleFinishDay = async () => {
  if (isDemoMode.value && currentWorkDay.value) {
    syncExistingShift({
      ...currentWorkDay.value,
      endTime: getNowTime(),
      status: 'Finished',
    }, null);
    return;
  }
  await finishDay();
};

onMounted(() => {
  // Пробуем запросить реальный API. Если сервер ещё не запущен, включаем демо-режим
  checkCanStart().catch(() => {
    isDemoMode.value = true;
  });
});
</script>

<style scoped>
.app-container {
  max-width: 760px;
  margin: 30px auto;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  color: #2c3e50;
  padding: 0 16px;
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 2px solid #e2e8f0;
  padding-bottom: 12px;
  margin-bottom: 24px;
}

.header h2 {
  margin: 0;
  font-size: 22px;
}

.user-info {
  font-size: 14px;
  color: #64748b;
  font-weight: 500;
}

.main-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
}

.mode-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #f8fafc;
  padding: 10px 14px;
  border-radius: 8px;
  margin-bottom: 20px;
}

.toggle-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  cursor: pointer;
}

.badge {
  font-size: 12px;
  padding: 4px 10px;
  border-radius: 12px;
  font-weight: 600;
}

.badge-info {
  background: #e0f2fe;
  color: #0369a1;
}

.badge-warning {
  background: #fef3c7;
  color: #b45309;
}

.alert {
  padding: 12px 16px;
  border-radius: 8px;
  margin-bottom: 16px;
  font-size: 14px;
}

.alert-error {
  background: #fee2e2;
  color: #b91c1c;
  border-left: 4px solid #ef4444;
}

.alert-warning {
  background: #fef3c7;
  color: #b45309;
  border-left: 4px solid #f59e0b;
}

.status-panel {
  text-align: center;
  padding: 20px;
  background: #f8fafc;
  border-radius: 10px;
  margin-bottom: 24px;
}

.status-title {
  font-size: 13px;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 6px;
}

.status-value {
  font-size: 24px;
  font-weight: 700;
}

.status-default { color: #64748b; }
.status-working { color: #16a34a; }
.status-break { color: #d97706; }
.status-finished { color: #475569; }
.status-blocked { color: #dc2626; }

.actions-panel {
  display: flex;
  gap: 12px;
  justify-content: center;
  margin-bottom: 28px;
}

.button-wrapper {
  display: inline-block;
}

.btn {
  padding: 12px 20px;
  font-size: 15px;
  font-weight: 600;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  filter: grayscale(40%);
}

.btn-start {
  background: #16a34a;
  color: #ffffff;
}
.btn-start:not(:disabled):hover { background: #15803d; }

.btn-break-start {
  background: #f59e0b;
  color: #ffffff;
}
.btn-break-start:not(:disabled):hover { background: #d97706; }

.btn-break-finish {
  background: #0284c7;
  color: #ffffff;
}
.btn-break-finish:not(:disabled):hover { background: #0369a1; }

.btn-finish {
  background: #dc2626;
  color: #ffffff;
}
.btn-finish:not(:disabled):hover { background: #b91c1c; }

.details-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
  border-top: 1px solid #e2e8f0;
  padding-top: 20px;
}

.info-box {
  background: #f8fafc;
  padding: 12px;
  border-radius: 8px;
  text-align: center;
}

.info-label {
  display: block;
  font-size: 12px;
  color: #64748b;
  margin-bottom: 4px;
}

.info-data {
  font-size: 16px;
  font-weight: 600;
  color: #0f172a;
}
</style>