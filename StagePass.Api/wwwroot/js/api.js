async function apiFetch(path, options = {}) {
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  const response = await fetch(path, { ...options, headers });

  if (response.status === 401) {
    // If on a page that requires auth, redirect
    const err = new Error('Unauthorized');
    err.status = 401;
    err.errorCode = 'AuthRequired';
    throw err;
  }

  if (!response.ok) {
    let body = {};
    try { body = await response.json(); } catch {}
    const msgs = body.errors ? Object.values(body.errors).flat().join(' ') : null;
    const err = new Error(msgs || body.error || body.message || body.title || `HTTP ${response.status}`);
    err.status = response.status;
    err.errorCode = body.error || null;
    err.body = body;
    throw err;
  }

  if (response.status === 204) return null;
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}
window.apiFetch = apiFetch;
