(function () {
  let host = document.querySelector('.toast-host');

  function getHost() {
    if (!host) {
      host = document.querySelector('.toast-host');
      if (!host) {
        host = document.createElement('div');
        host.className = 'toast-host';
        if (document.body) {
          document.body.appendChild(host);
        } else {
          document.addEventListener('DOMContentLoaded', () => {
            if (!host.parentElement && document.body) {
              document.body.appendChild(host);
            }
          });
        }
      }
    }
    return host;
  }

  window.showToast = function (message, type = 'info', duration = 3500) {
    const container = getHost();
    const t = document.createElement('div');
    const mappedType = type === 'success' ? 'ok' : type === 'error' ? 'bad' : type;
    t.className = `toast ${mappedType}`;
    const icons = { ok: '✓', bad: '✕', info: 'ℹ', success: '✓', error: '✕' };
    t.innerHTML = `<span style="font-weight:800;font-size:1rem">${icons[type] || '●'}</span><span></span>`;
    t.querySelector('span:last-child').textContent = message;
    container.appendChild(t);
    setTimeout(() => {
      t.classList.add('out');
      setTimeout(() => t.remove(), 250);
    }, duration);
  };

  if (document.body) {
    getHost();
  } else {
    document.addEventListener('DOMContentLoaded', getHost);
  }
})();
