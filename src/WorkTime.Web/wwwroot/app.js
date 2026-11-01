'use strict';

const dom = {
  error: document.getElementById('error'),
  addEmployee: document.getElementById('add-employee'),
  employeeName: document.getElementById('employee-name'),
  employees: document.getElementById('employees'),
  employeesEmpty: document.getElementById('employees-empty'),
  reportForm: document.getElementById('report-form'),
  reportFrom: document.getElementById('report-from'),
  reportTo: document.getElementById('report-to'),
  reportEmployee: document.getElementById('report-employee'),
  report: document.getElementById('report'),
};

// Введённое, но ещё не отправленное описание задачи: список перерисовывается, а текст терять нельзя.
const drafts = new Map();

// Параметры показанного отчёта. После старта и стопа обновляем именно его:
// поля формы пользователь мог уже изменить, но ещё не применить.
let shownReportQuery = null;

// --- Вспомогательные функции ---

async function api(path, { method = 'GET', body } = {}) {
  const response = await fetch(`/api${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail ?? problem?.title ?? `Ошибка сервера (${response.status})`);
  }

  return response.json();
}

// Весь текст попадает в DOM через textContent, поэтому имена и описания не могут внедрить разметку.
function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  Object.assign(node, props);
  node.append(...children.filter((child) => child !== null && child !== undefined));
  return node;
}

function showError(message) {
  dom.error.textContent = message;
  dom.error.hidden = false;
}

function clearError() {
  dom.error.hidden = true;
}

function formatDuration(totalSeconds) {
  const pad = (value) => String(value).padStart(2, '0');
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  return `${pad(hours)}:${pad(minutes)}:${pad(totalSeconds % 60)}`;
}

function formatDateTime(value) {
  return new Date(value).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'medium' });
}

function toDateInputValue(date) {
  const pad = (value) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

// Полночь по часам пользователя. Конструктор Date учитывает переход на летнее время.
function localMidnight(dateInputValue, addDays = 0) {
  const [year, month, day] = dateInputValue.split('-').map(Number);
  return new Date(year, month - 1, day + addDays);
}

// Ответы могут прийти не в том порядке, в каком ушли запросы. Показываем только ответ на последний запрос.
function latestOnly(load, render) {
  let latest = 0;
  return async (...args) => {
    const request = ++latest;
    const result = await load(...args);
    if (request === latest) {
      render(result, ...args);
    }
  };
}

// Запускает действие, блокируя кнопку на время запроса, чтобы двойной клик не отправил его дважды.
async function runAction(button, action) {
  clearError();
  if (button) button.disabled = true;
  try {
    await action();
  } catch (error) {
    showError(error.message);
    // Причиной могло стать устаревшее состояние, например таймер запущен в другой вкладке.
    await refreshAll().catch(() => {});
  } finally {
    if (button) button.disabled = false;
  }
}

// --- Сотрудники и таймеры ---

function renderRunning(employee) {
  const timer = employee.activeTimer;
  const clock = el('span', { className: 'clock', textContent: formatDuration(timer.elapsedSeconds) });
  clock.dataset.elapsed = timer.elapsedSeconds;
  clock.dataset.renderedAt = Date.now();

  const stop = el('button', { type: 'button', className: 'stop', textContent: 'Стоп' });
  stop.addEventListener('click', () => runAction(stop, async () => {
    await api(`/employees/${employee.id}/timer/stop`, { method: 'POST' });
    await refreshAll();
  }));

  return el('div', { className: 'running' },
    el('span', { className: 'running-task', textContent: timer.description }),
    el('span', { className: 'running-side' }, clock, stop));
}

function renderStartForm(employee) {
  const input = el('input', {
    type: 'text',
    maxLength: 500,
    required: true,
    placeholder: 'Над чем работает?',
    ariaLabel: `Описание задачи: ${employee.name}`,
    autocomplete: 'off',
    value: drafts.get(employee.id) ?? '',
  });
  input.dataset.draftFor = employee.id;
  input.addEventListener('input', () => drafts.set(employee.id, input.value));

  const start = el('button', { type: 'submit', textContent: 'Старт' });
  const form = el('form', { className: 'row' }, input, start);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    runAction(start, async () => {
      await api(`/employees/${employee.id}/timer/start`, {
        method: 'POST',
        body: { description: input.value },
      });
      drafts.delete(employee.id);
      await refreshAll();
    });
  });
  return form;
}

function renderEmployees(employees) {
  // Поля создаются заново, поэтому курсор в том поле, где сейчас печатают, нужно вернуть на место.
  const focused = document.activeElement;
  const focusedDraftFor = focused?.dataset?.draftFor;
  const caret = focused?.selectionStart;

  dom.employeesEmpty.hidden = employees.length > 0;
  dom.employees.replaceChildren(...employees.map((employee) =>
    el('li', { className: 'employee' },
      el('span', { className: 'employee-name', textContent: employee.name }),
      employee.activeTimer ? renderRunning(employee) : renderStartForm(employee))));

  if (focusedDraftFor) {
    const input = dom.employees.querySelector(`[data-draft-for="${focusedDraftFor}"]`);
    input?.focus();
    input?.setSelectionRange(caret, caret);
  }

  const selected = dom.reportEmployee.value;
  dom.reportEmployee.replaceChildren(
    el('option', { value: '', textContent: 'Все' }),
    ...employees.map((employee) => el('option', { value: employee.id, textContent: employee.name })));
  dom.reportEmployee.value = selected;
}

const loadEmployees = latestOnly(() => api('/employees'), renderEmployees);

// Секунды отсчитываем от значения сервера. Date.now, в отличие от performance.now,
// на всех платформах продолжает идти, пока компьютер спит.
function tickClocks() {
  document.querySelectorAll('.clock').forEach((clock) => {
    const passed = (Date.now() - Number(clock.dataset.renderedAt)) / 1000;
    clock.textContent = formatDuration(Number(clock.dataset.elapsed) + Math.floor(passed));
  });
}

// --- Отчёт ---

function renderEntry(entry) {
  return el('tr', {},
    el('td', { className: 'task', textContent: entry.description }),
    el('td', { className: 'time', textContent: formatDateTime(entry.startedAt) }),
    entry.isRunning
      ? el('td', { className: 'time badge', textContent: 'идёт' })
      : el('td', { className: 'time', textContent: formatDateTime(entry.endedAt) }),
    el('td', { className: 'duration', textContent: formatDuration(entry.secondsInPeriod) }));
}

function renderEmployeeReport(row) {
  const header = el('div', { className: 'report-employee-header' },
    el('h3', { textContent: row.employeeName }),
    el('span', { className: 'report-employee-total', textContent: formatDuration(row.totalSeconds) }));

  if (row.entries.length === 0) {
    return el('div', { className: 'report-employee' }, header,
      el('p', { className: 'empty', textContent: 'Нет записей за этот период.' }));
  }

  const table = el('table', {},
    el('thead', {}, el('tr', {},
      el('th', { textContent: 'Задача' }),
      el('th', { textContent: 'Начало' }),
      el('th', { textContent: 'Конец' }),
      el('th', { className: 'duration', textContent: 'В периоде' }))),
    el('tbody', {}, ...row.entries.map(renderEntry)));

  return el('div', { className: 'report-employee' }, header, el('div', { className: 'table-scroll' }, table));
}

function renderReport(report) {
  if (report.employees.length === 0) {
    dom.report.replaceChildren(el('p', { className: 'empty', textContent: 'Сотрудников пока нет, отчёт пуст.' }));
    return;
  }

  // Правая граница в API не включается, поэтому последний день периода — за миллисекунду до неё.
  const lastDay = new Date(new Date(report.to).getTime() - 1);
  const period = `${new Date(report.from).toLocaleDateString('ru-RU')} — ${lastDay.toLocaleDateString('ru-RU')}`;

  const parts = [
    el('p', { className: 'report-period', textContent: `Период: ${period}` }),
    ...report.employees.map(renderEmployeeReport),
    el('div', { className: 'report-total' },
      el('span', { textContent: 'Итого за период' }),
      el('span', { textContent: formatDuration(report.totalSeconds) })),
  ];

  const hasRunning = report.employees.some((row) => row.entries.some((entry) => entry.isRunning));
  if (hasRunning) {
    parts.push(el('p', { className: 'note', textContent: `Идущие таймеры посчитаны на ${formatDateTime(report.generatedAt)}.` }));
  }

  dom.report.replaceChildren(...parts);
}

function buildReportQuery() {
  const { value: fromValue } = dom.reportFrom;
  const { value: toValue } = dom.reportTo;
  if (!fromValue || !toValue) {
    throw new Error('Укажите обе даты периода.');
  }
  if (fromValue > toValue) {
    throw new Error('Дата «с» не может быть позже даты «по».');
  }

  // Дата «по» входит в период, а API принимает правую границу не включительно.
  const params = new URLSearchParams({
    from: localMidnight(fromValue).toISOString(),
    to: localMidnight(toValue, 1).toISOString(),
  });
  if (dom.reportEmployee.value) {
    params.set('employeeId', dom.reportEmployee.value);
  }
  return params.toString();
}

const loadReport = latestOnly(
  (query) => api(`/reports/time?${query}`),
  (report, query) => {
    renderReport(report);
    shownReportQuery = query;
  });

function showReportFromForm() {
  return loadReport(buildReportQuery());
}

function applyPreset(preset) {
  const today = new Date();
  let from = today;
  if (preset === 'week') {
    const daysSinceMonday = (today.getDay() + 6) % 7;
    from = new Date(today.getFullYear(), today.getMonth(), today.getDate() - daysSinceMonday);
  } else if (preset === 'month') {
    from = new Date(today.getFullYear(), today.getMonth(), 1);
  }
  dom.reportFrom.value = toDateInputValue(from);
  dom.reportTo.value = toDateInputValue(today);
}

async function refreshAll() {
  await loadEmployees();
  if (shownReportQuery) {
    await loadReport(shownReportQuery);
  }
}

// --- Запуск ---

dom.addEmployee.addEventListener('submit', (event) => {
  event.preventDefault();
  runAction(event.submitter, async () => {
    await api('/employees', { method: 'POST', body: { name: dom.employeeName.value } });
    dom.employeeName.value = '';
    await refreshAll();
  });
});

dom.reportForm.addEventListener('submit', (event) => {
  event.preventDefault();
  runAction(event.submitter, showReportFromForm);
});

document.querySelectorAll('[data-preset]').forEach((button) => {
  button.addEventListener('click', () => {
    applyPreset(button.dataset.preset);
    runAction(button, showReportFromForm);
  });
});

// Пока вкладка была скрыта, таймеры могли изменить в другой вкладке.
document.addEventListener('visibilitychange', () => {
  if (!document.hidden) {
    runAction(null, refreshAll);
  }
});

applyPreset('month');
setInterval(tickClocks, 1000);
runAction(null, loadEmployees);
