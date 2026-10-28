const API_BASE = '';  // same origin — empty string means relative URLs

async function apiFetch(path, options = {}) {
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  let response;
  try {
    response = await fetch(API_BASE + path, { ...options, headers });
  } catch (err) {
    if (typeof window.showToast === 'function') {
      window.showToast(err.message || 'Network error occurred.', 'error');
    }
    throw err;
  }

  if (!response.ok) {
    let body = {};
    try { body = await response.json(); } catch {}
    let validationMessage = null;
    if (body && body.errors && typeof body.errors === 'object') {
      const msgs = Object.values(body.errors).flat().filter(Boolean);
      if (msgs.length > 0) {
        validationMessage = msgs.join(' ');
      }
    }
    const err = new Error(body.message || validationMessage || body.error || body.title || 'Request failed. Please check your input and try again.');
    err.status = response.status;
    err.errorCode = body.error || null;
    err.validationErrors = body.errors || null;
    err.body = body;
    throw err;
  }
  if (response.status === 204) return null;
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

window.apiFetch = apiFetch;
