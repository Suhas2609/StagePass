(function () {
  'use strict';

  // Stored in JS module scope only (never in localStorage or sessionStorage).
  let apiKey = '';

  const errorDisplay = document.getElementById('error-display');
  const statusBanner = document.getElementById('organizer-status');
  const apiKeyInput = document.getElementById('api-key-input');
  const connectBtn = document.getElementById('connect-btn');
  const dashboard = document.getElementById('organizer-dashboard');
  const statsRow = document.getElementById('stats-row');

  const createEventForm = document.getElementById('create-event-form');
  const eventResult = document.getElementById('event-result');

  const createPerformanceForm = document.getElementById('create-performance-form');
  const perfResult = document.getElementById('perf-result');

  const createTicketTypeForm = document.getElementById('create-tickettype-form');
  const ttResult = document.getElementById('tt-result');

  const viewBookingsBtn = document.getElementById('view-bookings-btn');
  const cancelPerfBtn = document.getElementById('cancel-perf-btn');
  const bookingsTableSection = document.getElementById('bookings-table');
  const inventorySummarySection = document.getElementById('inventory-summary');
  const bookingsPerformanceInput = document.getElementById('bookings-performance-id');

  function formatDateTime(value) {
    const str = String(value);
    const d = new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(str) ? str : str + 'Z');
    return d.toLocaleString('en-GB', {
      dateStyle: 'medium',
      timeStyle: 'short'
    });
  }

  function clearFeedback() {
    errorDisplay.textContent = '';
    statusBanner.hidden = true;
    statusBanner.textContent = '';
  }

  function showStatus(msg) {
    statusBanner.textContent = msg;
    statusBanner.hidden = false;
  }

  function mapOrganizerError(err) {
    if (err.status === 401) {
      return 'Invalid API key. Please check the key and reconnect.';
    }
    switch (err.errorCode) {
      case 'OrganizerNotFound':
        return 'Organizer ID was not found.';
      case 'VenueNotFound':
        return 'Venue ID was not found.';
      case 'StartDateTimeMustBeInFuture':
        return 'Performance start date and time must be in the future.';
      case 'EventNotFound':
        return 'Event ID was not found.';
      case 'PerformanceNotFound':
        return 'Performance ID was not found.';
      case 'TicketTypeNameAlreadyExists':
        return 'A ticket type with this name already exists for this performance.';
      case 'PerformanceCancelled':
        return 'This performance is already cancelled.';
      default:
        return err.message || 'The request could not be completed.';
    }
  }

  function handleError(err) {
    errorDisplay.textContent = mapOrganizerError(err);
  }

  function authHeaders() {
    return { 'X-Api-Key': apiKey };
  }

  function createStatCard(label, value, subText) {
    const card = document.createElement('div');
    card.className = 'stat-card fade-in';

    const lbl = document.createElement('div');
    lbl.className = 'stat-label';
    lbl.textContent = label;

    const val = document.createElement('div');
    val.className = 'stat-value';
    val.innerHTML = value;

    card.append(lbl, val);

    if (subText) {
      const sub = document.createElement('div');
      sub.className = 'stat-sub';
      sub.textContent = subText;
      card.appendChild(sub);
    }

    return card;
  }

  function renderStats(summaryList, bookingsList) {
    statsRow.innerHTML = '';

    const summary = summaryList || [];
    const bookings = bookingsList || [];

    const totalSold = summary.reduce((s, t) => s + (t.sold || 0), 0);
    const totalCapacity = summary.reduce((s, t) => s + (t.capacity || 0), 0);
    const totalAvailable = summary.reduce((s, t) => s + (t.available || 0), 0);
    const pctUsed = totalCapacity > 0 ? Math.round((totalSold / totalCapacity) * 100) : 0;

    statsRow.appendChild(createStatCard('Total Sold', String(totalSold)));
    statsRow.appendChild(createStatCard('Available', String(totalAvailable)));
    statsRow.appendChild(createStatCard('Capacity Used', pctUsed + '%'));
    statsRow.appendChild(createStatCard('Confirmed Bookings', String(bookings.length)));
  }

  function renderInventorySummary(summaryList) {
    inventorySummarySection.innerHTML = '';

    const heading = document.createElement('h3');
    heading.style.fontSize = '1.05rem';
    heading.style.fontWeight = '700';
    heading.style.marginBottom = 'var(--space-md)';
    heading.textContent = 'Inventory Breakdown';
    inventorySummarySection.appendChild(heading);

    if (!summaryList || summaryList.length === 0) {
      const p = document.createElement('p');
      p.style.color = 'var(--text-secondary)';
      p.textContent = 'No ticket types defined for this performance.';
      inventorySummarySection.appendChild(p);
      return;
    }

    const grid = document.createElement('div');
    grid.style.display = 'grid';
    grid.style.gridTemplateColumns = 'repeat(auto-fill, minmax(220px, 1fr))';
    grid.style.gap = 'var(--space-md)';

    for (const item of summaryList) {
      const card = document.createElement('div');
      card.className = 'stat-card';

      const label = document.createElement('div');
      label.className = 'stat-label';
      label.textContent = item.ticketTypeName;

      const capacity = item.capacity || 0;
      const available = item.available || 0;
      const sold = capacity - available;
      const pctSold = capacity > 0 ? Math.min(100, Math.round((sold / capacity) * 100)) : 0;

      const value = document.createElement('div');
      value.className = 'stat-value';
      value.style.fontSize = '1.5rem';
      value.innerHTML = `${available} <span style="font-size:0.85rem; font-weight:500; color:var(--text-secondary);">/ ${capacity} left</span>`;

      const sub = document.createElement('div');
      sub.className = 'stat-sub';
      sub.textContent = `${sold} sold (${pctSold}% filled)`;

      const bar = document.createElement('div');
      bar.className = 'inv-bar';

      const fill = document.createElement('div');
      fill.className = 'inv-bar-fill';
      fill.style.width = `${pctSold}%`;

      if (available === 0) {
        fill.classList.add('zero');
      } else if (available < capacity * 0.2) {
        fill.classList.add('low');
      }

      bar.appendChild(fill);
      card.append(label, value, sub, bar);
      grid.appendChild(card);
    }

    inventorySummarySection.appendChild(grid);
  }

  function renderBookingsTable(bookings) {
    bookingsTableSection.innerHTML = '';

    const heading = document.createElement('h3');
    heading.style.fontSize = '1.05rem';
    heading.style.fontWeight = '700';
    heading.style.margin = 'var(--space-xl) 0 var(--space-md)';
    heading.textContent = 'Confirmed Bookings';
    bookingsTableSection.appendChild(heading);

    if (!bookings || bookings.length === 0) {
      const p = document.createElement('p');
      p.style.color = 'var(--text-secondary)';
      p.textContent = 'No confirmed bookings for this performance yet.';
      bookingsTableSection.appendChild(p);
      return;
    }

    const tableWrapper = document.createElement('div');
    tableWrapper.style.overflowX = 'auto';

    const table = document.createElement('table');
    table.className = 'data-table';

    const thead = document.createElement('thead');
    const headerRow = document.createElement('tr');
    for (const col of ['Booking Reference', 'Customer', 'Ticket Type', 'Quantity', 'Date']) {
      const th = document.createElement('th');
      th.textContent = col;
      headerRow.appendChild(th);
    }
    thead.appendChild(headerRow);
    table.appendChild(thead);

    const tbody = document.createElement('tbody');
    for (const b of bookings) {
      const tr = document.createElement('tr');

      const tdRef = document.createElement('td');
      const code = document.createElement('code');
      code.style.fontFamily = "'Courier New', monospace";
      code.style.fontWeight = '700';
      code.style.color = 'var(--brand)';
      code.textContent = b.bookingReference;
      tdRef.appendChild(code);

      const tdCustomer = document.createElement('td');
      tdCustomer.textContent = b.customerIdentifier;

      const tdType = document.createElement('td');
      tdType.textContent = b.ticketTypeName;

      const tdQty = document.createElement('td');
      tdQty.textContent = String(b.quantity);

      const tdDate = document.createElement('td');
      tdDate.textContent = formatDateTime(b.bookingCreatedAt);

      tr.append(tdRef, tdCustomer, tdType, tdQty, tdDate);
      tbody.appendChild(tr);
    }

    table.appendChild(tbody);
    tableWrapper.appendChild(table);
    bookingsTableSection.appendChild(tableWrapper);
  }

  async function loadPerformanceBookings(perfId) {
    clearFeedback();
    try {
      const data = await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/bookings`, {
        method: 'GET',
        headers: authHeaders()
      });

      renderStats(data.summary || [], data.bookings || []);
      renderInventorySummary(data.summary || []);
      renderBookingsTable(data.bookings || []);
    } catch (err) {
      handleError(err);
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    connectBtn.addEventListener('click', () => {
      clearFeedback();
      const entered = apiKeyInput.value.trim();
      if (!entered) {
        errorDisplay.textContent = 'Please enter an API key.';
        return;
      }
      apiKey = entered;
      dashboard.hidden = false;
      showStatus('Connected to Organizer Dashboard.');
    });

    createEventForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      clearFeedback();
      if (eventResult) eventResult.textContent = '';

      const payload = {
        title: document.getElementById('event-title').value.trim(),
        category: document.getElementById('event-category').value.trim(),
        description: document.getElementById('event-description').value.trim(),
        venueId: parseInt(document.getElementById('event-venue-id').value, 10),
        organizerId: parseInt(document.getElementById('event-organizer-id').value, 10)
      };

      try {
        const res = await window.apiFetch('/api/organizer/events', {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify(payload)
        });
        if (typeof window.showToast === 'function') {
          window.showToast(`Event created: ID ${res.eventId}`, 'success');
        }
        // Auto-populate perf-event-id
        const perfEventInput = document.getElementById('perf-event-id');
        if (perfEventInput) perfEventInput.value = String(res.eventId);
        showStatus(`Event created successfully (Event ID: ${res.eventId}).`);
      } catch (err) {
        handleError(err);
      }
    });

    createPerformanceForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      clearFeedback();
      if (perfResult) perfResult.textContent = '';

      const rawStart = document.getElementById('perf-start').value;
      const startIso = rawStart ? new Date(rawStart).toISOString() : '';
      const payload = {
        eventId: parseInt(document.getElementById('perf-event-id').value, 10),
        startDateTime: startIso
      };

      try {
        const res = await window.apiFetch('/api/organizer/performances', {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify(payload)
        });
        if (typeof window.showToast === 'function') {
          window.showToast(`Performance created: ID ${res.performanceId}`, 'success');
        }
        // Auto-populate tt-perf-id and bookings-performance-id
        const ttPerfInput = document.getElementById('tt-perf-id');
        if (ttPerfInput) ttPerfInput.value = String(res.performanceId);
        if (bookingsPerformanceInput) bookingsPerformanceInput.value = String(res.performanceId);
        showStatus(`Performance created successfully (Performance ID: ${res.performanceId}).`);
      } catch (err) {
        handleError(err);
      }
    });

    createTicketTypeForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      clearFeedback();
      if (ttResult) ttResult.textContent = '';

      const perfId = parseInt(document.getElementById('tt-perf-id').value, 10);
      const payload = {
        name: document.getElementById('tt-name').value.trim(),
        price: parseFloat(document.getElementById('tt-price').value),
        capacity: parseInt(document.getElementById('tt-capacity').value, 10)
      };

      try {
        const res = await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/tickettypes`, {
          method: 'POST',
          headers: authHeaders(),
          body: JSON.stringify(payload)
        });
        if (typeof window.showToast === 'function') {
          window.showToast(`Ticket type created: ID ${res.ticketTypeId}`, 'success');
        }
        showStatus(`Ticket type added successfully (Ticket Type ID: ${res.ticketTypeId}).`);
      } catch (err) {
        handleError(err);
      }
    });

    if (viewBookingsBtn) {
      viewBookingsBtn.addEventListener('click', () => {
        const perfId = parseInt(bookingsPerformanceInput.value, 10);
        if (Number.isNaN(perfId) || perfId < 1) {
          errorDisplay.textContent = 'Please enter a valid Performance ID.';
          return;
        }
        loadPerformanceBookings(perfId);
      });
    }

    if (cancelPerfBtn) {
      cancelPerfBtn.addEventListener('click', async () => {
        clearFeedback();
        const perfId = parseInt(bookingsPerformanceInput.value, 10);
        if (Number.isNaN(perfId) || perfId < 1) {
          errorDisplay.textContent = 'Please enter a valid Performance ID.';
          return;
        }
        if (!window.confirm(`Are you sure you want to cancel performance #${perfId}? All pending holds will be cancelled and further reservations blocked.`)) {
          return;
        }

        try {
          await window.apiFetch(`/api/organizer/performances/${encodeURIComponent(perfId)}/cancel`, {
            method: 'PATCH',
            headers: authHeaders()
          });
          if (typeof window.showToast === 'function') {
            window.showToast(`Performance #${perfId} cancelled`, 'success');
          }
          showStatus(`Performance #${perfId} has been successfully cancelled.`);
          loadPerformanceBookings(perfId);
        } catch (err) {
          handleError(err);
        }
      });
    }
  });
})();
