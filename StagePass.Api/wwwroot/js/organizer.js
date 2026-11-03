(function () {
  'use strict';

  const authGate = document.getElementById('auth-gate');
  const dashLayout = document.getElementById('dash-layout');
  const dashUserName = document.getElementById('dash-user-name');

  const statBookings = document.getElementById('stat-bookings');
  const statSold = document.getElementById('stat-sold');
  const statHolds = document.getElementById('stat-holds');
  const statAvail = document.getElementById('stat-avail');

  const invPerfId = document.getElementById('inv-perf-id');
  const invLoadBtn = document.getElementById('inv-load-btn');
  const invResults = document.getElementById('inv-results');

  const bookingsPerfId = document.getElementById('bookings-perf-id');
  const loadBookingsBtn = document.getElementById('load-bookings-btn');
  const cancelPerfBtn = document.getElementById('cancel-perf-btn');
  const bookingsInvSummary = document.getElementById('bookings-inv-summary');
  const bookingsTableWrap = document.getElementById('bookings-table-wrap');

  const formEvent = document.getElementById('form-event');
  const evTitle = document.getElementById('ev-title');
  const evCategory = document.getElementById('ev-category');
  const evDescription = document.getElementById('ev-description');
  const evVenue = document.getElementById('ev-venue');
  const evOrganizer = document.getElementById('ev-organizer');
  const evResult = document.getElementById('ev-result');

  const formPerformance = document.getElementById('form-performance');
  const pfEvent = document.getElementById('pf-event');
  const pfStart = document.getElementById('pf-start');
  const pfResult = document.getElementById('pf-result');

  const formTicketType = document.getElementById('form-tickettype');
  const ttPerf = document.getElementById('tt-perf');
  const ttName = document.getElementById('tt-name');
  const ttPrice = document.getElementById('tt-price');
  const ttCapacity = document.getElementById('tt-capacity');
  const ttResult = document.getElementById('tt-result');

  const bkPerfId = document.getElementById('bk-perf-id');
  const bkLoadBtn = document.getElementById('bk-load-btn');
  const bkSummary = document.getElementById('bk-summary');
  const bkTableWrap = document.getElementById('bk-table-wrap');

  function parseUtcDate(value) {
    const str = String(value);
    return new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(str) ? str : str + 'Z');
  }

  function formatDateTime(value) {
    if (!value) return '—';
    return parseUtcDate(value).toLocaleString('en-GB', {
      dateStyle: 'medium',
      timeStyle: 'short'
    });
  }

  function showFormFeedback(targetEl, message, isError = false) {
    if (!targetEl) return;
    targetEl.innerHTML = '';
    const box = document.createElement('div');
    box.className = isError ? 'alert alert-bad' : 'form-success';
    if (isError) box.style.marginTop = 'var(--sp-4)';
    box.textContent = message;
    targetEl.appendChild(box);
  }

  function renderInventory(summary, targetEl) {
    if (!targetEl) return;
    targetEl.innerHTML = '';

    const list = Array.isArray(summary) ? summary : [];
    if (list.length === 0) {
      const p = document.createElement('p');
      p.style.color = 'var(--text-low)';
      p.style.fontSize = 'var(--f-sm)';
      p.textContent = 'No ticket tiers configured for this performance.';
      targetEl.appendChild(p);
      return;
    }

    list.forEach(tier => {
      const capacity = Number(tier.capacity || 0);
      const sold = Number(tier.sold || 0);
      const pct = capacity > 0 ? Math.min(100, Math.round((sold / capacity) * 100)) : 0;
      const fillClass = pct >= 90 ? 'low' : pct >= 60 ? 'mid' : '';

      const row = document.createElement('div');
      row.className = 'inv-row';

      const nameEl = document.createElement('div');
      nameEl.className = 'inv-name';
      nameEl.textContent = tier.ticketTypeName || 'Tier';

      const barWrap = document.createElement('div');
      barWrap.className = 'inv-bar-wrap';

      const barFill = document.createElement('div');
      barFill.className = `inv-bar-fill ${fillClass}`.trim();
      barFill.style.width = `${pct}%`;

      barWrap.appendChild(barFill);

      const numbersEl = document.createElement('div');
      numbersEl.className = 'inv-numbers';
      numbersEl.textContent = `${sold}/${capacity} sold`;

      row.append(nameEl, barWrap, numbersEl);
      targetEl.appendChild(row);
    });
  }

  function renderBookingsTable(bookings, targetEl) {
    if (!targetEl) return;
    targetEl.innerHTML = '';

    const list = Array.isArray(bookings) ? bookings : [];
    if (list.length === 0) {
      const p = document.createElement('p');
      p.style.color = 'var(--text-low)';
      p.style.fontSize = 'var(--f-sm)';
      p.textContent = 'No confirmed bookings for this performance yet.';
      targetEl.appendChild(p);
      return;
    }

    const wrap = document.createElement('div');
    wrap.className = 'data-table-wrap';

    const table = document.createElement('table');
    table.className = 'data-table';

    const thead = document.createElement('thead');
    const hRow = document.createElement('tr');
    ['Reference', 'Customer', 'Ticket Type', 'Qty', 'Date'].forEach(col => {
      const th = document.createElement('th');
      th.textContent = col;
      hRow.appendChild(th);
    });
    thead.appendChild(hRow);
    table.appendChild(thead);

    const tbody = document.createElement('tbody');
    list.forEach(b => {
      const tr = document.createElement('tr');

      const tdRef = document.createElement('td');
      tdRef.className = 'ref-cell';
      tdRef.textContent = b.bookingReference;

      const tdCustomer = document.createElement('td');
      tdCustomer.textContent = b.customerIdentifier;

      const tdTier = document.createElement('td');
      tdTier.textContent = b.ticketTypeName;

      const tdQty = document.createElement('td');
      tdQty.textContent = String(b.quantity);

      const tdDate = document.createElement('td');
      tdDate.textContent = formatDateTime(b.bookingCreatedAt);

      tr.append(tdRef, tdCustomer, tdTier, tdQty, tdDate);
      tbody.appendChild(tr);
    });

    table.appendChild(tbody);
    wrap.appendChild(table);
    targetEl.appendChild(wrap);
  }

  function resetOverviewStats() {
    if (statBookings) statBookings.textContent = '—';
    if (statSold) statSold.textContent = '—';
    if (statHolds) statHolds.textContent = '—';
    if (statAvail) statAvail.textContent = '—';
  }

  async function loadVenues() {
    if (!evVenue) return;
    try {
      const venues = await window.apiFetch('/api/organizer/venues');
      if (!Array.isArray(venues) || venues.length === 0) return;
      evVenue.innerHTML = '';
      venues.forEach((v, idx) => {
        const opt = document.createElement('option');
        opt.value = String(v.venueId);
        opt.textContent = `${v.name} — ${v.city}`;
        if (idx === 0) opt.selected = true;
        evVenue.appendChild(opt);
      });
    } catch {
      // Fail silently and leave the select showing "Loading venues…"
    }
  }

  async function loadOverviewInventory() {
    const perfId = parseInt(invPerfId ? invPerfId.value : '', 10);
    if (Number.isNaN(perfId) || perfId < 1) {
      if (typeof window.showToast === 'function') {
        window.showToast('Please enter a valid Performance ID.', 'bad');
      }
      return;
    }

    try {
      const data = await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/bookings`);
      const bookings = data.bookings || [];
      const summary = data.summary || [];

      renderInventory(summary, invResults);

      if (statBookings) statBookings.textContent = String(bookings.length);
      if (statSold) statSold.textContent = String(summary.reduce((s, t) => s + (t.sold || 0), 0));
      if (statAvail) statAvail.textContent = String(summary.reduce((s, t) => s + (t.available || 0), 0));
      if (statHolds) statHolds.textContent = '—';
    } catch (err) {
      if (typeof window.showToast === 'function') {
        window.showToast(err.message || 'Unable to load inventory.', 'bad');
      }
    }
  }

  async function loadBookingsAndInventory(perfIdInput, summaryTarget, tableTarget) {
    const perfId = parseInt(perfIdInput ? perfIdInput.value : '', 10);
    if (Number.isNaN(perfId) || perfId < 1) {
      if (typeof window.showToast === 'function') {
        window.showToast('Please enter a valid Performance ID.', 'bad');
      }
      return;
    }

    try {
      const data = await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/bookings`);
      renderInventory(data.summary || [], summaryTarget);
      renderBookingsTable(data.bookings || [], tableTarget);
    } catch (err) {
      if (typeof window.showToast === 'function') {
        window.showToast(err.message || 'Unable to load bookings.', 'bad');
      }
    }
  }

  async function cancelPerformance() {
    const perfId = parseInt(bookingsPerfId ? bookingsPerfId.value : '', 10);
    if (Number.isNaN(perfId) || perfId < 1) {
      if (typeof window.showToast === 'function') {
        window.showToast('Please enter a valid Performance ID first.', 'bad');
      }
      return;
    }

    if (!window.confirm('Cancel this performance? This will release all pending holds.')) {
      return;
    }

    try {
      await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/cancel`, {
        method: 'PATCH'
      });
      if (typeof window.showToast === 'function') {
        window.showToast('Performance cancelled. All pending holds released.', 'ok');
      }
      await loadBookingsAndInventory(bookingsPerfId, bookingsInvSummary, bookingsTableWrap);
    } catch (err) {
      if (err.status === 409) {
        if (typeof window.showToast === 'function') {
          window.showToast('Performance is already cancelled.', 'bad');
        }
      } else if (typeof window.showToast === 'function') {
        window.showToast(err.message || 'Could not cancel performance.', 'bad');
      }
    }
  }

  function initSidebarNav() {
    const navItems = document.querySelectorAll('.dash-nav-item[data-page]');
    const pages = document.querySelectorAll('.dash-page');

    navItems.forEach(btn => {
      btn.addEventListener('click', () => {
        const target = btn.dataset.page;
        navItems.forEach(item => item.classList.remove('active'));
        btn.classList.add('active');

        pages.forEach(page => {
          page.classList.toggle('active', page.id === `page-${target}`);
        });

        if (target === 'events') {
          if (bookingsInvSummary) bookingsInvSummary.innerHTML = '';
          if (bookingsTableWrap) {
            bookingsTableWrap.innerHTML = '<p style="color:var(--text-low);font-size:var(--f-sm);">Enter a Performance ID above to view bookings.</p>';
          }
        } else if (target === 'bookings') {
          if (bkSummary) bkSummary.innerHTML = '';
          if (bkTableWrap) {
            bkTableWrap.innerHTML = '<p style="color:var(--text-low);font-size:var(--f-sm);">Enter a Performance ID to view its bookings.</p>';
          }
        }
      });
    });
  }

  function initCreateForms() {
    if (formEvent) {
      formEvent.addEventListener('submit', async (e) => {
        e.preventDefault();
        const title = evTitle ? evTitle.value.trim() : '';
        if (!title) {
          showFormFeedback(evResult, 'Please enter an event title.', true);
          return;
        }

        const payload = {
          title,
          category: evCategory ? evCategory.value : 'Music',
          description: evDescription ? evDescription.value.trim() : '',
          venueId: parseInt(evVenue ? evVenue.value : '1', 10) || 1,
          organizerId: parseInt(evOrganizer ? evOrganizer.value : '1', 10) || 1
        };

        try {
          const res = await window.apiFetch('/api/organizer/events', {
            method: 'POST',
            body: JSON.stringify(payload)
          });
          showFormFeedback(evResult, `Event created! ID: ${res.eventId}`);
          if (pfEvent) pfEvent.value = String(res.eventId);
          if (typeof window.showToast === 'function') {
            window.showToast('Event created successfully.', 'ok');
          }
        } catch (err) {
          showFormFeedback(evResult, err.message || 'Could not create event.', true);
        }
      });
    }

    if (formPerformance) {
      formPerformance.addEventListener('submit', async (e) => {
        e.preventDefault();
        const eventId = parseInt(pfEvent ? pfEvent.value : '', 10);
        const rawStart = pfStart ? pfStart.value : '';

        if (Number.isNaN(eventId) || eventId < 1 || !rawStart) {
          showFormFeedback(pfResult, 'Please provide a valid Event ID and future Start Date & Time.', true);
          return;
        }

        const payload = {
          eventId,
          startDateTime: new Date(rawStart).toISOString()
        };

        try {
          const res = await window.apiFetch('/api/organizer/performances', {
            method: 'POST',
            body: JSON.stringify(payload)
          });
          showFormFeedback(pfResult, `Performance scheduled! ID: ${res.performanceId}`);
          if (ttPerf) ttPerf.value = String(res.performanceId);
          if (bkPerfId) bkPerfId.value = String(res.performanceId);
          if (bookingsPerfId) bookingsPerfId.value = String(res.performanceId);
          if (invPerfId) invPerfId.value = String(res.performanceId);
          if (typeof window.showToast === 'function') {
            window.showToast('Performance scheduled.', 'ok');
          }
        } catch (err) {
          showFormFeedback(pfResult, err.message || 'Could not schedule performance.', true);
        }
      });
    }

    if (formTicketType) {
      formTicketType.addEventListener('submit', async (e) => {
        e.preventDefault();
        const perfId = parseInt(ttPerf ? ttPerf.value : '', 10);
        const name = ttName ? ttName.value.trim() : '';
        const price = parseFloat(ttPrice ? ttPrice.value : '');
        const capacity = parseInt(ttCapacity ? ttCapacity.value : '', 10);

        if (Number.isNaN(perfId) || perfId < 1 || !name || Number.isNaN(price) || price < 0 || Number.isNaN(capacity) || capacity < 1) {
          showFormFeedback(ttResult, 'Please fill in all ticket tier fields with valid values.', true);
          return;
        }

        try {
          const res = await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/tickettypes`, {
            method: 'POST',
            body: JSON.stringify({ name, price, capacity })
          });
          showFormFeedback(ttResult, `Ticket type "${name}" created! ID: ${res.ticketTypeId}`);
          if (typeof window.showToast === 'function') {
            window.showToast('Ticket type added.', 'ok');
          }
        } catch (err) {
          showFormFeedback(ttResult, err.message || 'Could not add ticket type.', true);
        }
      });
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    const isOrganizer = window.Auth && window.Auth.isLoggedIn() && window.Auth.isOrganizer();

    if (!isOrganizer) {
      if (dashLayout) dashLayout.hidden = true;
      if (authGate) authGate.hidden = false;
      return;
    }

    if (authGate) authGate.hidden = true;
    if (dashLayout) dashLayout.hidden = false;

    const user = window.Auth.getUser();
    if (dashUserName && user && user.name) {
      dashUserName.textContent = user.name;
    }
    if (evOrganizer && user && user.userId) {
      evOrganizer.value = String(user.userId);
    }

    loadVenues();
    resetOverviewStats();
    initSidebarNav();
    initCreateForms();

    if (invLoadBtn) {
      invLoadBtn.addEventListener('click', loadOverviewInventory);
    }

    if (loadBookingsBtn) {
      loadBookingsBtn.addEventListener('click', () => {
        loadBookingsAndInventory(bookingsPerfId, bookingsInvSummary, bookingsTableWrap);
      });
    }

    if (bkLoadBtn) {
      bkLoadBtn.addEventListener('click', () => {
        loadBookingsAndInventory(bkPerfId, bkSummary, bkTableWrap);
      });
    }

    if (cancelPerfBtn) {
      cancelPerfBtn.addEventListener('click', cancelPerformance);
    }
  });
})();
