(function () {
  'use strict';

  // Client-side inventory checks are for UX convenience only; server responses are authoritative.
  let idempotencyKey = crypto.randomUUID();
  let currentPerformance = null;
  let currentHold = null;
  let lastBookingReference = null;
  let countdownInterval = null;

  const errorDisplay = document.getElementById('error-display');
  const performanceDetail = document.getElementById('performance-detail');
  const ticketTypesSection = document.getElementById('ticket-types');
  const reservationForm = document.getElementById('reservation-form');
  const ticketTypeSelect = document.getElementById('ticket-type-select');
  const quantityInput = document.getElementById('quantity-input');
  const customerEmailInput = document.getElementById('customer-email');
  const qtyError = document.getElementById('qty-error');
  const emailError = document.getElementById('email-error');
  const holdStatusSection = document.getElementById('hold-status');
  const holdUntilHeading = document.getElementById('hold-until-heading');
  const holdSummary = document.getElementById('hold-summary');
  const countdownLabel = document.getElementById('countdown-label');
  const ringFill = document.getElementById('ring-fill');
  const confirmBookingBtn = document.getElementById('confirm-booking-btn');
  const retryHoldBtn = document.getElementById('retry-hold-btn');

  function updateStep(step) {
    const s1 = document.getElementById('step-1');
    const s2 = document.getElementById('step-2');
    const s3 = document.getElementById('step-3');
    if (!s1 || !s2 || !s3) return;

    if (step === 2) {
      s1.className = 'step done';
      s2.className = 'step active';
      s3.className = 'step';
    } else if (step === 3) {
      s1.className = 'step done';
      s2.className = 'step done';
      s3.className = 'step active';
    } else {
      s1.className = 'step active';
      s2.className = 'step';
      s3.className = 'step';
    }
  }

  function renderDetailSkeletons() {
    performanceDetail.innerHTML = `
      <div class="performance-hero" style="pointer-events:none">
        <div class="skeleton" style="height:2rem; width:60%; margin-bottom:16px;"></div>
        <div class="skeleton" style="height:1.2rem; width:120px; margin-bottom:16px; border-radius:100px;"></div>
        <div class="skeleton" style="height:1rem; width:85%; margin-bottom:12px;"></div>
        <div class="skeleton" style="height:1rem; width:70%; margin-bottom:24px;"></div>
        <div style="display:flex; gap:24px;">
          <div class="skeleton" style="height:1rem; width:180px;"></div>
          <div class="skeleton" style="height:1rem; width:180px;"></div>
        </div>
      </div>
    `;
    ticketTypesSection.innerHTML = `
      <div class="ticket-card" style="pointer-events:none">
        <div class="skeleton" style="height:1.2rem; width:60%; margin-bottom:12px;"></div>
        <div class="skeleton" style="height:1.6rem; width:40%; margin-bottom:8px;"></div>
        <div class="skeleton" style="height:0.9rem; width:50%;"></div>
      </div>
      <div class="ticket-card" style="pointer-events:none">
        <div class="skeleton" style="height:1.2rem; width:60%; margin-bottom:12px;"></div>
        <div class="skeleton" style="height:1.6rem; width:40%; margin-bottom:8px;"></div>
        <div class="skeleton" style="height:0.9rem; width:50%;"></div>
      </div>
      <div class="ticket-card" style="pointer-events:none">
        <div class="skeleton" style="height:1.2rem; width:60%; margin-bottom:12px;"></div>
        <div class="skeleton" style="height:1.6rem; width:40%; margin-bottom:8px;"></div>
        <div class="skeleton" style="height:0.9rem; width:50%;"></div>
      </div>
    `;
  }

  function parseUtcDate(value) {
    const str = String(value);
    return new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(str) ? str : str + 'Z');
  }

  function formatDateTime(value) {
    return parseUtcDate(value).toLocaleString('en-GB', {
      dateStyle: 'full',
      timeStyle: 'short'
    });
  }

  function formatTimeHms(value) {
    return parseUtcDate(value).toLocaleTimeString('en-GB', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false
    });
  }

  function showError(message) {
    errorDisplay.textContent = message;
  }

  function clearError() {
    errorDisplay.textContent = '';
    quantityInput.classList.remove('input-error');
    customerEmailInput.classList.remove('input-error');
    if (qtyError) qtyError.textContent = '';
    if (emailError) emailError.textContent = '';
  }

  function stopCountdown() {
    if (countdownInterval) {
      clearInterval(countdownInterval);
      countdownInterval = null;
    }
  }

  function startCountdown(expiresAtIso) {
    stopCountdown();
    const expiresAtMs = parseUtcDate(expiresAtIso).getTime();
    const totalDurationSeconds = Math.max(1, Math.round((expiresAtMs - Date.now()) / 1000));

    function tick() {
      const remainingMs = expiresAtMs - Date.now();
      if (remainingMs <= 0) {
        stopCountdown();
        holdStatusSection.hidden = true;
        holdUntilHeading.textContent = '';
        holdSummary.textContent = '';
        if (countdownLabel) countdownLabel.textContent = '00:00';
        if (ringFill) ringFill.style.strokeDashoffset = '339.3';
        if (countdownLabel) countdownLabel.classList.remove('urgent', 'critical');
        if (ringFill) ringFill.classList.remove('urgent', 'critical');
        showError('Your reservation has expired. Please select tickets again.');
        currentHold = null;
        idempotencyKey = crypto.randomUUID();
        reservationForm.hidden = false;
        updateStep(2);
        return;
      }

      const secondsRemaining = Math.max(0, Math.ceil(remainingMs / 1000));
      const mins = String(Math.floor(secondsRemaining / 60)).padStart(2, '0');
      const secs = String(secondsRemaining % 60).padStart(2, '0');
      if (countdownLabel) countdownLabel.textContent = `${mins}:${secs}`;

      // Progress ring animation: stroke-dashoffset = 339.3 * (1 - secondsRemaining / totalDurationSeconds)
      const fraction = Math.max(0, Math.min(1, secondsRemaining / totalDurationSeconds));
      const offset = 339.3 * (1 - fraction);
      if (ringFill) ringFill.style.strokeDashoffset = offset.toFixed(1);

      // Urgency styling: .urgent when < 60s, .critical when < 20s
      if (countdownLabel) countdownLabel.classList.remove('urgent', 'critical');
      if (ringFill) ringFill.classList.remove('urgent', 'critical');

      if (secondsRemaining < 20) {
        if (countdownLabel) countdownLabel.classList.add('critical');
        if (ringFill) ringFill.classList.add('critical');
      } else if (secondsRemaining < 60) {
        if (countdownLabel) countdownLabel.classList.add('urgent');
        if (ringFill) ringFill.classList.add('urgent');
      }
    }

    tick();
    countdownInterval = setInterval(tick, 1000);
  }

  function displayHold(hold) {
    currentHold = hold;
    holdUntilHeading.textContent = `Reservation held until ${formatTimeHms(hold.expiresAt)}`;
    holdSummary.textContent = `${hold.quantity} × ${hold.ticketTypeName} (Hold #${hold.holdId})`;
    reservationForm.hidden = true;
    holdStatusSection.hidden = false;
    startCountdown(hold.expiresAt);
    updateStep(3);

    if (typeof window.showToast === 'function') {
      window.showToast('Tickets reserved! Confirm within 5 minutes.', 'success');
    }
  }

  function renderPerformance(perf) {
    performanceDetail.innerHTML = '';

    const hero = document.createElement('div');
    hero.className = 'performance-hero fade-in';
    const category = (perf.category || '').toLowerCase();
    if (category) {
      hero.setAttribute('data-category', category);
    }

    const title = document.createElement('h1');
    title.textContent = perf.eventTitle;

    const badge = document.createElement('span');
    badge.className = 'badge';
    const totalAvail = (perf.ticketTypes || []).reduce((sum, t) => sum + (t.available || 0), 0);
    if (perf.status === 'Cancelled') {
      badge.classList.add('badge-cancelled');
      badge.textContent = 'Cancelled';
    } else if (totalAvail === 0) {
      badge.classList.add('badge-sold-out');
      badge.textContent = 'Sold Out';
    } else {
      badge.classList.add('badge-upcoming');
      badge.textContent = perf.status || 'Upcoming';
    }

    const desc = document.createElement('p');
    desc.className = 'description';
    desc.textContent = perf.eventDescription || 'No description provided.';

    const metaGrid = document.createElement('div');
    metaGrid.className = 'meta-grid';

    // Venue Item
    const venueItem = document.createElement('div');
    venueItem.className = 'meta-item';
    const vIcon = document.createElement('span');
    vIcon.textContent = '📍';
    const vText = document.createElement('span');
    const vStrong = document.createElement('strong');
    vStrong.textContent = 'Venue: ';
    vText.append(vStrong, document.createTextNode(`${perf.venueName}${perf.venueAddress ? ' — ' + perf.venueAddress : ''}, ${perf.city}`));
    venueItem.append(vIcon, vText);

    // Date Item
    const dateItem = document.createElement('div');
    dateItem.className = 'meta-item';
    const dIcon = document.createElement('span');
    dIcon.textContent = '🗓️';
    const dText = document.createElement('span');
    const dStrong = document.createElement('strong');
    dStrong.textContent = 'Date: ';
    dText.append(dStrong, document.createTextNode(formatDateTime(perf.startDateTime)));
    dateItem.append(dIcon, dText);

    metaGrid.append(venueItem, dateItem);

    hero.append(title, badge, desc, metaGrid);
    performanceDetail.appendChild(hero);

    ticketTypesSection.innerHTML = '';
    ticketTypeSelect.innerHTML = '';

    const ticketTypes = perf.ticketTypes || [];
    let firstAvailableSelected = false;

    ticketTypes.forEach((tt) => {
      const isSoldOut = tt.available === 0;
      const card = document.createElement('div');
      card.className = 'ticket-card';
      if (isSoldOut) {
        card.classList.add('sold-out');
      }
      card.dataset.id = String(tt.ticketTypeId);

      const radio = document.createElement('input');
      radio.type = 'radio';
      radio.name = 'ticketTypeRadio';
      radio.value = String(tt.ticketTypeId);
      radio.id = `tt-radio-${tt.ticketTypeId}`;
      if (isSoldOut) {
        radio.disabled = true;
      }

      const nameEl = document.createElement('div');
      nameEl.className = 'tier-name';
      nameEl.textContent = tt.name;

      const priceEl = document.createElement('div');
      priceEl.className = 'tier-price';
      priceEl.textContent = `£${Number(tt.price).toFixed(2)}`;

      const availEl = document.createElement('div');
      availEl.className = 'tier-avail';
      if (isSoldOut) {
        availEl.classList.add('sold');
        availEl.textContent = 'Sold out';
      } else if (tt.available <= 10) {
        availEl.classList.add('low');
        availEl.textContent = `Only ${tt.available} left!`;
      } else {
        availEl.textContent = `${tt.available} available`;
      }

      card.append(radio, nameEl, priceEl, availEl);

      function selectTier() {
        if (isSoldOut) return;
        ticketTypesSection.querySelectorAll('.ticket-card').forEach(c => c.classList.remove('selected'));
        card.classList.add('selected');
        radio.checked = true;
        ticketTypeSelect.value = String(tt.ticketTypeId);
      }

      card.addEventListener('click', selectTier);
      radio.addEventListener('click', (e) => {
        e.stopPropagation();
        selectTier();
      });

      ticketTypesSection.appendChild(card);

      const opt = document.createElement('option');
      opt.value = String(tt.ticketTypeId);
      opt.textContent = `${tt.name} — £${Number(tt.price).toFixed(2)} (${isSoldOut ? 'Sold out' : tt.available + ' available'})`;
      if (isSoldOut) {
        opt.disabled = true;
      }
      ticketTypeSelect.appendChild(opt);

      // Select first non-sold-out tier by default
      if (!isSoldOut && !firstAvailableSelected) {
        card.classList.add('selected');
        radio.checked = true;
        ticketTypeSelect.value = String(tt.ticketTypeId);
        firstAvailableSelected = true;
      }
    });

    ticketTypeSelect.addEventListener('change', () => {
      const selectedId = ticketTypeSelect.value;
      ticketTypesSection.querySelectorAll('.ticket-card').forEach(c => {
        const isMatch = c.dataset.id === selectedId;
        c.classList.toggle('selected', isMatch);
        const r = c.querySelector('input[type="radio"]');
        if (r && !r.disabled) r.checked = isMatch;
      });
    });

    if (perf.status === 'Cancelled') {
      showError('This performance has been cancelled. Reservations are closed.');
      reservationForm.hidden = true;
    } else if (totalAvail === 0) {
      showError('This performance is sold out.');
      reservationForm.hidden = true;
    } else {
      reservationForm.hidden = false;
    }

    // Step 1 done, Step 2 active
    updateStep(2);
  }

  async function submitReservation() {
    clearError();
    if (!currentPerformance) return;

    const selectedTypeId = parseInt(ticketTypeSelect.value, 10);
    const selectedType = (currentPerformance.ticketTypes || []).find(t => t.ticketTypeId === selectedTypeId);
    const qty = parseInt(quantityInput.value, 10);
    const email = customerEmailInput.value.trim();

    let hasValidationError = false;

    if (Number.isNaN(qty) || qty < 1 || qty > 10) {
      quantityInput.classList.add('input-error');
      if (qtyError) qtyError.textContent = 'Quantity must be between 1 and 10.';
      hasValidationError = true;
    } else if (selectedType && qty > selectedType.available) {
      quantityInput.classList.add('input-error');
      if (qtyError) qtyError.textContent = `Only ${selectedType.available} ticket(s) currently available.`;
      hasValidationError = true;
    }

    if (!email || !email.includes('@')) {
      customerEmailInput.classList.add('input-error');
      if (emailError) emailError.textContent = 'Please enter a valid email address.';
      hasValidationError = true;
    }

    if (hasValidationError) {
      showError('Please correct the errors in the form before submitting.');
      return;
    }

    if (!idempotencyKey) {
      idempotencyKey = crypto.randomUUID();
    }

    try {
      const hold = await window.apiFetch('/api/reservations', {
        method: 'POST',
        body: JSON.stringify({
          ticketTypeId: selectedTypeId,
          quantity: qty,
          idempotencyKey: idempotencyKey,
          customerIdentifier: email
        })
      });
      displayHold(hold);
    } catch (err) {
      if (err.status === 409) {
        if (err.errorCode === 'InsufficientInventory') {
          showError('No tickets available. Please try a different quantity or tier.');
          return;
        }
        if (err.errorCode === 'PerformanceCancelled') {
          showError('This performance has been cancelled.');
          return;
        }
        if (err.errorCode === 'IdempotencyKeyReused') {
          showError('This request ID has already been used with different parameters.');
          return;
        }
      }
      if (err.status === 400) {
        showError(err.message || 'Please check the reservation details and try again.');
        return;
      }
      showError(err.message || 'Unable to reserve tickets. Please try again.');
    }
  }

  async function confirmBooking() {
    clearError();
    if (!currentHold) return;

    const email = customerEmailInput.value.trim();
    try {
      const result = await window.apiFetch(`/api/reservations/${encodeURIComponent(currentHold.holdId)}/confirm`, {
        method: 'POST',
        body: JSON.stringify({
          customerIdentifier: email
        })
      });
      if (result && result.bookingReference) {
        lastBookingReference = result.bookingReference;
        stopCountdown();
        window.location.href = `booking.html?ref=${encodeURIComponent(result.bookingReference)}`;
      }
    } catch (err) {
      if (err.status === 409) {
        if (err.errorCode === 'HoldExpired') {
          stopCountdown();
          holdStatusSection.hidden = true;
          currentHold = null;
          idempotencyKey = crypto.randomUUID();
          reservationForm.reset();
          reservationForm.hidden = false;
          showError('Your reservation expired. Please start again.');
          updateStep(2);
          return;
        }
        if (err.errorCode === 'AlreadyConfirmed') {
          if (lastBookingReference) {
            window.location.href = `booking.html?ref=${encodeURIComponent(lastBookingReference)}`;
          } else {
            showError('This reservation has already been confirmed.');
          }
          return;
        }
        if (err.errorCode === 'InvalidOwner') {
          showError('Confirmation failed: email does not match the reservation.');
          return;
        }
      }
      showError(err.message || 'Could not confirm booking. Please try again.');
    }
  }

  document.addEventListener('DOMContentLoaded', async () => {
    const params = new URLSearchParams(window.location.search);
    const id = params.get('id');
    if (!id) {
      showError('Performance not found. Please select a valid event from the home page.');
      return;
    }

    renderDetailSkeletons();

    try {
      const perf = await window.apiFetch(`/api/performances/${encodeURIComponent(id)}`);
      currentPerformance = perf;
      renderPerformance(perf);
    } catch (err) {
      performanceDetail.innerHTML = '';
      ticketTypesSection.innerHTML = '';
      if (err.status === 404) {
        showError('Performance not found.');
      } else {
        showError(err.message || 'Unable to load performance details.');
      }
    }

    reservationForm.addEventListener('submit', (e) => {
      e.preventDefault();
      submitReservation();
    });

    if (retryHoldBtn) {
      retryHoldBtn.addEventListener('click', () => {
        submitReservation();
      });
    }

    confirmBookingBtn.addEventListener('click', () => {
      confirmBooking();
    });
  });
})();
