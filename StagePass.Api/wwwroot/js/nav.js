(function () {
  function initNav() {
    const nav = document.querySelector('.site-nav');
    if (!nav) return;

    // Scroll effect
    const onScroll = () => {
      nav.classList.toggle('scrolled', window.scrollY > 20);
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();

    // Auth state in nav
    const authZone = nav.querySelector('.nav-auth-zone');
    if (!authZone) return;

    if (window.Auth && window.Auth.isLoggedIn()) {
      const user = window.Auth.getUser();
      const initials = (user.name || 'U').split(' ').map(w => w[0]).join('').slice(0, 2).toUpperCase();
      authZone.innerHTML = `
        <div class="nav-user">
          <div class="nav-avatar">${initials}</div>
          <span class="nav-user-name">${user.name.split(' ')[0]}</span>
          <div class="nav-dropdown">
            ${user.isOrganizer ? `<a href="dashboard.html">Organizer Dashboard</a>` : ''}
            <a href="index.html">Browse Events</a>
            <div class="nav-divider"></div>
            <button id="logout-btn">Sign out</button>
          </div>
        </div>`;

      document.getElementById('logout-btn')?.addEventListener('click', async () => {
        try { await window.apiFetch('/api/auth/logout', { method: 'POST' }); } catch {}
        window.Auth.clearSession();
        window.location.href = 'index.html';
      });
    } else {
      authZone.innerHTML = `
        <a href="login.html" class="btn btn-ghost btn-sm">Log in</a>
        <a href="register.html" class="btn btn-brand btn-sm">Sign up</a>`;
    }

    // Hamburger
    const hamburger = nav.querySelector('.nav-hamburger');
    const mobileMenu = nav.querySelector('.nav-mobile-menu');
    hamburger?.addEventListener('click', () => {
      mobileMenu?.classList.toggle('open');
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initNav);
  } else {
    initNav();
  }
})();
