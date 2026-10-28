(function() {
  let container = null;

  function getContainer() {
    if (!container) {
      container = document.querySelector('.toast-container');
      if (!container) {
        container = document.createElement('div');
        container.className = 'toast-container';
        if (document.body) {
          document.body.appendChild(container);
        } else {
          document.addEventListener('DOMContentLoaded', () => {
            if (!container.parentElement && document.body) {
              document.body.appendChild(container);
            }
          });
        }
      }
    }
    return container;
  }

  window.showToast = function(message, type = 'info', durationMs = 3500) {
    const c = getContainer();
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    const icon = type === 'success' ? '✓' : type === 'error' ? '✗' : 'ℹ';
    toast.innerHTML = `<span style="font-weight:700">${icon}</span><span></span>`;
    toast.querySelector('span:last-child').textContent = message;
    c.appendChild(toast);
    setTimeout(() => {
      toast.classList.add('fade-out');
      setTimeout(() => toast.remove(), 300);
    }, durationMs);
  };

  if (document.body) {
    getContainer();
  } else {
    document.addEventListener('DOMContentLoaded', getContainer);
  }
})();
