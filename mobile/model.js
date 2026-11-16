(function (scope) {
  'use strict';

  function clampPercent(value) {
    if (value === null || value === undefined || typeof value === 'boolean' || (typeof value === 'string' && !value.trim())) return null;
    const number = Number(value);
    if (!Number.isFinite(number)) return null;
    return Math.max(0, Math.min(100, number));
  }

  function validDate(value) {
    return typeof value === 'string' && value.length > 0 && Number.isFinite(Date.parse(value));
  }

  function formatPercent(value) {
    const percentage = clampPercent(value);
    if (percentage === null) return '—';
    if (percentage > 0 && percentage < 1) return '<1%';
    return `${Math.round(percentage)}%`;
  }

  function normalizeWindow(value, expectedKind) {
    if (value === null || value === undefined) return null;
    if (typeof value !== 'object' || typeof value.kind !== 'string') throw new Error('额度窗口格式无效');
    if (expectedKind && value.kind !== expectedKind) throw new Error('额度窗口类型无效');
    const remainingPercent = clampPercent(value.remainingPercent);
    if (remainingPercent === null) throw new Error('额度百分比无效');
    if (value.resetsAt !== null && value.resetsAt !== undefined && !validDate(value.resetsAt)) throw new Error('恢复时间无效');
    return {
      kind: value.kind,
      label: typeof value.label === 'string' ? value.label.slice(0, 60) : value.kind,
      remainingPercent,
      resetsAt: value.resetsAt || null
    };
  }

  const KNOWN_IDS = ['personal', 'work', 'claude'];

  function nonNegative(value) {
    if (value === null || value === undefined || typeof value === 'boolean' || (typeof value === 'string' && !value.trim())) return null;
    const number = Number(value);
    return Number.isFinite(number) && number >= 0 ? number : null;
  }

  function normalizeLocalUsage(value) {
    if (!value || typeof value !== 'object') return null;
    const result = {};
    for (const period of ['today', 'week']) {
      const entry = value[period];
      if (!entry || typeof entry !== 'object') return null;
      const totalTokens = nonNegative(entry.totalTokens);
      if (totalTokens === null) return null;
      result[period] = { totalTokens, costUsd: nonNegative(entry.costUsd) };
    }
    return result;
  }

  function formatTokens(value) {
    const number = nonNegative(value);
    if (number === null) return '—';
    if (number >= 1e9) return `${(number / 1e9).toFixed(1)}B`;
    if (number >= 1e6) return `${(number / 1e6).toFixed(1)}M`;
    if (number >= 1e3) return `${(number / 1e3).toFixed(1)}K`;
    return String(Math.round(number));
  }

  function formatUsd(value) {
    const number = nonNegative(value);
    if (number === null) return '—';
    if (number > 0 && number < 0.01) return '<$0.01';
    return `$${number.toFixed(2)}`;
  }

  function validateSnapshot(value) {
    if (!value || value.version !== 1 || value.type !== 'usage_snapshot') throw new Error('不支持的额度消息');
    if (!validDate(value.publishedAt) || !Array.isArray(value.profiles)) throw new Error('额度消息格式无效');
    // Unknown future account ids are skipped instead of rejecting the whole snapshot.
    const profiles = value.profiles.filter((profile) => profile && KNOWN_IDS.includes(profile.id)).map((profile) => {
      if (typeof profile.ok !== 'boolean') throw new Error('账号数据格式无效');
      if (profile.fetchedAt !== null && profile.fetchedAt !== undefined && !validDate(profile.fetchedAt)) throw new Error('账号更新时间无效');
      const longTerm = normalizeWindow(profile.longTerm);
      if (longTerm && !['weekly', 'workspace'].includes(longTerm.kind)) throw new Error('长周期类型无效');
      return {
        id: profile.id,
        label: typeof profile.label === 'string' && profile.label.trim() ? profile.label.trim().slice(0, 40) : profile.id,
        ok: profile.ok,
        status: profile.status === 'ok' ? 'ok' : 'error',
        fetchedAt: profile.fetchedAt || null,
        fiveHour: normalizeWindow(profile.fiveHour, '5h'),
        longTerm,
        localUsage: normalizeLocalUsage(profile.localUsage)
      };
    });
    return {
      version: 1,
      type: 'usage_snapshot',
      deviceId: typeof value.deviceId === 'string' ? value.deviceId.slice(0, 80) : '',
      deviceName: typeof value.deviceName === 'string' ? value.deviceName.slice(0, 80) : '',
      publishedAt: value.publishedAt,
      profiles
    };
  }

  function getFreshness(publishedAt, now = Date.now()) {
    const ageMs = Math.max(0, now - Date.parse(publishedAt));
    const minutes = ageMs / 60000;
    if (minutes < 2) return { level: 'fresh', minutes };
    if (minutes <= 10) return { level: 'aging', minutes };
    if (minutes <= 1440) return { level: 'stale', minutes };
    return { level: 'expired', minutes };
  }

  function formatCountdown(resetsAt, now = Date.now()) {
    if (!validDate(resetsAt)) return '未提供';
    const remainingMinutes = Math.ceil((Date.parse(resetsAt) - now) / 60000);
    if (remainingMinutes <= 0) return '等待电脑确认';
    if (remainingMinutes >= 1440) {
      const days = Math.floor(remainingMinutes / 1440);
      const hours = Math.floor((remainingMinutes % 1440) / 60);
      return `${days}天${hours}小时`;
    }
    if (remainingMinutes >= 60) return `${Math.floor(remainingMinutes / 60)}小时${remainingMinutes % 60}分`;
    return `${Math.max(1, remainingMinutes)}分钟`;
  }

  function formatAge(publishedAt, now = Date.now()) {
    const minutes = Math.max(0, Math.floor((now - Date.parse(publishedAt)) / 60000));
    if (minutes < 1) return '刚刚';
    if (minutes < 60) return `${minutes} 分钟前`;
    if (minutes < 1440) return `${Math.floor(minutes / 60)} 小时前`;
    return `${Math.floor(minutes / 1440)} 天前`;
  }

  function quotaTone(value, stale) {
    const percentage = clampPercent(value);
    if (stale || percentage === null) return 'muted';
    if (percentage <= 15) return 'danger';
    if (percentage <= 35) return 'warning';
    return 'normal';
  }

  scope.CodexMobileModel = Object.freeze({ clampPercent, formatPercent, formatTokens, formatUsd, validateSnapshot, getFreshness, formatCountdown, formatAge, quotaTone });
})(globalThis);
