(function () {
  const KEY = 'sp_session';

  function getSession() {
    try { return JSON.parse(localStorage.getItem(KEY)); } catch { return null; }
  }
  function setSession(data) {
    localStorage.setItem(KEY, JSON.stringify(data));
  }
  function clearSession() {
    localStorage.removeItem(KEY);
  }
  function isLoggedIn() {
    const s = getSession();
    return s && s.token && new Date(s.expiresAt) > new Date();
  }
  function getToken() {
    return getSession()?.token ?? null;
  }
  function getUser() {
    return getSession() ?? null;
  }
  function isOrganizer() {
    return getSession()?.isOrganizer === true;
  }

  // Attach Bearer token to apiFetch automatically
  const _origApiFetch = window.apiFetch;
  window.apiFetch = async function (path, options = {}) {
    const token = getToken();
    if (token) {
      options = { ...options };
      options.headers = { ...(options.headers || {}) };
      options.headers['Authorization'] = `Bearer ${token}`;
    }
    return _origApiFetch(path, options);
  };

  // Export
  window.Auth = { getSession, setSession, clearSession, isLoggedIn, getToken, getUser, isOrganizer };
})();
