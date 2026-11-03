(function () {
  'use strict';

  let idempotencyKey = crypto.randomUUID();
  let currentPerformance = null;
  let selectedTier = null;
  let quantity = 1;
  let currentHold = null;
  let lastBookingReference = null;
  let countdownInterval = null;

  const pageTitle = document.getElementById('page-title');
  const perfHero = document.getElementById('perf-hero');
  const perfCat = document.getElementById('perf-cat');
  const perfTitle = document.getElementById('perf-title');
  const perfMeta = document.getElementById('perf-meta');
  const perfDetails = document.getElementById('perf-details');

  const step1 = document.getElementById('step-1');
  const step2 = document.getElementById('step-2');
  const step3 = document.getElementById('step-3');

  const phaseSelect = document.getElementById('phase-select');
  const phaseHold = document.getElementById('phase-hold');
  const errorDisplay = document.getElementById('error-display');
  const tierList = document.getElementById('tier-list');
  const qtyDecBtn = document.getElementById('qty-dec');
  const qtyIncBtn = document.getElementById('qty-inc');
  const qtyDisplay = document.getElementById('qty-display');
  const customerEmailInput = document.getElementById('customer-email');
  const emailError = document.getElementById('email-error');
  const totalPriceEl = document.getElementById('total-price');
  const reserveBtn = document.getElementById('reserve-btn');

  const holdSummaryText = document.getElementById('hold-summary-text');
  const ringFill = document.getElementById('ring-fill');
  const ringLabel = document.getElementById('ring-label');
  const confirmBtn = document.getElementById('confirm-btn');
  const retryBtn = document.getElementById('retry-btn');

  function parseUtcDate(value) {
    if (value instanceof Date) return value;
    const str = String(value);
    return new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(str) ? str : str + 'Z');
  }

  function markStep(stepNum, state) {
    const el = document.getElementById(`step-${stepNum}`);
    if (!el) return;
    el.className = 'step-item' + (state ? ` ${state}` : '');
  }

  function formatFullDateTime(value) {
    if (!value) return '';
    return parseUtcDate(value).toLocaleString('en-GB', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  function setStep(activeStep) {
    if (!step1 || !step2 || !step3) return;
    step1.className = 'step-item';
    step2.className = 'step-item';
    step3.className = 'step-item';

    if (activeStep === 1) {
      step1.classList.add('active');
    } else if (activeStep === 2) {
      step1.classList.add('done');
      step2.classList.add('active');
    } else if (activeStep === 3) {
      step1.classList.add('done');
      step2.classList.add('done');
      step3.classList.add('active');
    }
  }

  function showError(msg) {
    if (errorDisplay) errorDisplay.textContent = msg;
  }

  function clearError() {
    if (errorDisplay) errorDisplay.textContent = '';
    if (emailError) emailError.textContent = '';
    if (customerEmailInput) customerEmailInput.classList.remove('error');
  }

  function updateStepperAndPrice() {
    if (!selectedTier) {
      if (qtyDisplay) qtyDisplay.textContent = '1';
      if (qtyDecBtn) qtyDecBtn.disabled = true;
      if (qtyIncBtn) qtyIncBtn.disabled = true;
      if (totalPriceEl) totalPriceEl.textContent = '—';
      if (reserveBtn) reserveBtn.disabled = true;
      return;
    }

    const maxQty = Math.min(10, selectedTier.available);
    if (quantity > maxQty) quantity = Math.max(1, maxQty);
    if (quantity < 1) quantity = 1;

    if (qtyDisplay) qtyDisplay.textContent = String(quantity);
    if (qtyDecBtn) qtyDecBtn.disabled = quantity <= 1;
    if (qtyIncBtn) qtyIncBtn.disabled = quantity >= maxQty;

    const total = quantity * Number(selectedTier.price || 0);
    if (totalPriceEl) totalPriceEl.textContent = `£${total.toFixed(2)}`;
    if (reserveBtn) reserveBtn.disabled = selectedTier.available <= 0;
  }

  function renderHero(perf) {
    const cat = (perf.category || 'default').toLowerCase();
    if (perfHero) perfHero.dataset.cat = cat;
    if (pageTitle) pageTitle.textContent = `${perf.eventTitle} — StagePass`;
    if (perfCat) perfCat.textContent = perf.category || 'Live Event';
    if (perfTitle) perfTitle.textContent = perf.eventTitle || 'Event Details';

    if (perfMeta) {
      perfMeta.innerHTML = '';

      const dateChip = document.createElement('div');
      dateChip.className = 'perf-meta-chip';
      dateChip.textContent = `🗓️ ${formatFullDateTime(perf.startDateTime)}`;

      const venueChip = document.createElement('div');
      venueChip.className = 'perf-meta-chip';
      const venueParts = [perf.venueName, perf.city].filter(Boolean);
      venueChip.textContent = `📍 ${venueParts.join(', ')}`;

      perfMeta.append(dateChip, venueChip);
    }
  }

  function createInfoItem(label, value) {
    const item = document.createElement('div');
    item.className = 'perf-info-item';

    const lbl = document.createElement('div');
    lbl.className = 'label';
    lbl.textContent = label;

    const val = document.createElement('div');
    val.className = 'value';
    val.textContent = value || '—';

    item.append(lbl, val);
    return item;
  }

  function renderDetails(perf) {
    if (!perfDetails) return;
    perfDetails.innerHTML = '';

    const heading = document.createElement('h2');
    heading.textContent = 'About This Event';

    const desc = document.createElement('p');
    desc.className = 'perf-description';
    desc.textContent = perf.eventDescription || 'Join us for an unforgettable live performance experience.';

    const grid = document.createElement('div');
    grid.className = 'perf-info-grid';

    grid.appendChild(createInfoItem('Date & Time', formatFullDateTime(perf.startDateTime)));
    grid.appendChild(createInfoItem('Venue', perf.venueName));
    grid.appendChild(createInfoItem('Address', [perf.venueAddress, perf.city].filter(Boolean).join(', ')));
    grid.appendChild(createInfoItem('Category', perf.category));

    perfDetails.append(heading, desc, grid);
  }

  function renderTiers(perf) {
    if (!tierList) return;
    tierList.innerHTML = '';
    selectedTier = null;

    const tiers = perf.ticketTypes || [];
    tiers.forEach((tt) => {
      const isSoldOut = tt.available <= 0;
      const card = document.createElement('div');
      card.className = 'tier-card';
      if (isSoldOut) card.classList.add('sold-out');
      card.dataset.tierId = String(tt.ticketTypeId);

      const radio = document.createElement('input');
      radio.type = 'radio';
      radio.name = 'ticket-tier';
      radio.value = String(tt.ticketTypeId);
      radio.disabled = isSoldOut;

      const dot = document.createElement('div');
      dot.className = 'tier-dot';

      const info = document.createElement('div');
      info.className = 'tier-info';

      const name = document.createElement('div');
      name.className = 'tier-name';
      name.textContent = tt.name;

      const avail = document.createElement('div');
      avail.className = 'tier-avail';
      if (isSoldOut) {
        avail.classList.add('sold');
        avail.textContent = 'Sold out';
      } else if (tt.available <= 10) {
        avail.classList.add('low');
        avail.textContent = `Only ${tt.available} left!`;
      } else {
        avail.textContent = `${tt.available} available`;
      }

      info.append(name, avail);

      const price = document.createElement('div');
      price.className = 'tier-price';
      price.textContent = `£${Number(tt.price).toFixed(2)}`;

      card.append(radio, dot, info, price);

      if (!isSoldOut && !selectedTier) {
        selectedTier = tt;
        card.classList.add('selected');
        radio.checked = true;
      }

      card.addEventListener('click', () => {
        if (isSoldOut) return;
        selectedTier = tt;
        tierList.querySelectorAll('.tier-card').forEach(c => c.classList.remove('selected'));
        card.classList.add('selected');
        radio.checked = true;
        clearError();
        updateStepperAndPrice();
      });

      tierList.appendChild(card);
    });

    if (perf.status === 'Cancelled') {
      showError('This performance has been cancelled. Reservations are closed.');
      if (reserveBtn) reserveBtn.disabled = true;
    } else if (!selectedTier) {
      showError('All ticket tiers for this performance are currently sold out.');
      if (reserveBtn) reserveBtn.disabled = true;
    }

    updateStepperAndPrice();
  }

  function stopCountdown() {
    if (countdownInterval) {
      clearInterval(countdownInterval);
      countdownInterval = null;
    }
  }

  function resetToSelectPhase(toastMessage) {
    stopCountdown();
    sessionStorage.removeItem('sp_hold');
    currentHold = null;
    idempotencyKey = crypto.randomUUID();
    if (phaseHold) phaseHold.classList.add('hidden');
    if (phaseSelect) phaseSelect.classList.remove('hidden');
    setStep(1);
    if (ringFill) {
      ringFill.style.strokeDashoffset = '0';
      ringFill.classList.remove('urgent', 'critical');
    }
    if (toastMessage && typeof window.showToast === 'function') {
      window.showToast(toastMessage, 'bad');
    }
  }

  function startCountdown(expiresAt) {
    stopCountdown();
    const expiresAtMs = parseUtcDate(expiresAt).getTime();

    function tick() {
      const remainingMs = expiresAtMs - Date.now();
      const secondsLeft = Math.max(0, Math.ceil(remainingMs / 1000));

      if (secondsLeft <= 0) {
        sessionStorage.removeItem('sp_hold');
        resetToSelectPhase('Hold expired');
        showError('Your 5-minute reservation hold expired. Please reserve again.');
        return;
      }

      const mins = String(Math.floor(secondsLeft / 60)).padStart(2, '0');
      const secs = String(secondsLeft % 60).padStart(2, '0');
      if (ringLabel) ringLabel.textContent = `${mins}:${secs}`;

      const fraction = Math.max(0, Math.min(1, secondsLeft / 300));
      const offset = 282.7 * (1 - fraction);
      if (ringFill) {
        ringFill.style.strokeDashoffset = offset.toFixed(1);
        ringFill.classList.remove('urgent', 'critical');
        if (secondsLeft < 20) {
          ringFill.classList.add('critical');
        } else if (secondsLeft < 60) {
          ringFill.classList.add('urgent');
        }
      }
    }

    tick();
    countdownInterval = setInterval(tick, 1000);
  }

  function displayHold(hold) {
    currentHold = hold;
    sessionStorage.setItem('sp_hold', JSON.stringify({
      holdId:         hold.holdId,
      expiresAt:      hold.expiresAt,
      quantity:       hold.quantity,
      ticketTypeId:   selectedTier ? selectedTier.ticketTypeId : hold.ticketTypeId,
      ticketTypeName: hold.ticketTypeName,
      customerEmail:  document.getElementById('customer-email').value,
      idempotencyKey: idempotencyKey,
      performanceId:  new URLSearchParams(location.search).get('id'),
    }));
    setStep(2);
    if (phaseSelect) phaseSelect.classList.add('hidden');
    if (phaseHold) phaseHold.classList.remove('hidden');
    if (holdSummaryText) {
      holdSummaryText.textContent = `${hold.quantity} × ${hold.ticketTypeName}`;
    }
    startCountdown(hold.expiresAt);
  }

  async function submitReservation(isRetry = false) {
    clearError();
    if (!currentPerformance || !selectedTier) {
      showError('Please select an available ticket tier.');
      return;
    }

    const email = customerEmailInput ? customerEmailInput.value.trim() : '';
    if (!email) {
      if (window.Auth && !window.Auth.isLoggedIn()) {
        const returnUrl = `performance.html?id=${encodeURIComponent(currentPerformance.performanceId)}`;
        window.location.href = `login.html?returnTo=${encodeURIComponent(returnUrl)}`;
        return;
      }
      if (customerEmailInput) customerEmailInput.classList.add('error');
      if (emailError) emailError.textContent = 'Please enter your email address.';
      return;
    }

    if (!email.includes('@')) {
      if (customerEmailInput) customerEmailInput.classList.add('error');
      if (emailError) emailError.textContent = 'Please enter a valid email address.';
      return;
    }

    if (quantity < 1 || quantity > 10 || quantity > selectedTier.available) {
      showError(`Please choose between 1 and ${Math.min(10, selectedTier.available)} tickets.`);
      return;
    }

    if (!isRetry && !currentHold) {
      idempotencyKey = crypto.randomUUID();
    }

    if (reserveBtn) reserveBtn.disabled = true;
    if (retryBtn) retryBtn.disabled = true;

    try {
      const hold = await window.apiFetch('/api/reservations', {
        method: 'POST',
        body: JSON.stringify({
          ticketTypeId: selectedTier.ticketTypeId,
          quantity: quantity,
          idempotencyKey: idempotencyKey,
          customerIdentifier: email
        })
      });

      displayHold(hold);
      if (typeof window.showToast === 'function') {
        window.showToast(isRetry ? 'Hold verified!' : 'Tickets held for 5 minutes!', 'ok');
      }
    } catch (err) {
      if (err.status === 409) {
        if (err.errorCode === 'HoldExpired') {
          sessionStorage.removeItem('sp_hold');
          resetToSelectPhase('Hold expired');
          showError('Your reservation hold has expired. Please select your tickets again.');
          return;
        }
        if (err.errorCode === 'InsufficientInventory') {
          showError('Not enough tickets left in this tier. Please try a smaller quantity or another tier.');
        } else if (err.errorCode === 'PerformanceCancelled') {
          showError('This performance has been cancelled.');
        } else {
          showError(err.message || 'Could not reserve tickets due to a conflict.');
        }
      } else {
        showError(err.message || 'Unable to reserve tickets. Please try again.');
      }
    } finally {
      if (reserveBtn) reserveBtn.disabled = false;
      if (retryBtn) retryBtn.disabled = false;
    }
  }

  async function confirmHold() {
    if (!currentHold) return;
    const email = (customerEmailInput && customerEmailInput.value.trim()) || currentHold.customerEmail || '';

    if (confirmBtn) {
      confirmBtn.disabled = true;
      confirmBtn.textContent = 'Confirming…';
    }

    try {
      const result = await window.apiFetch(`/api/reservations/${encodeURIComponent(currentHold.holdId)}/confirm`, {
        method: 'POST',
        body: JSON.stringify({
          customerIdentifier: email
        })
      });

      if (result && result.bookingReference) {
        lastBookingReference = result.bookingReference;
        sessionStorage.removeItem('sp_hold');
        stopCountdown();
        setStep(3);
        window.location.href = `booking.html?ref=${encodeURIComponent(result.bookingReference)}`;
      }
    } catch (err) {
      if (err.status === 409) {
        if (err.errorCode === 'HoldExpired') {
          sessionStorage.removeItem('sp_hold');
          resetToSelectPhase('Hold expired');
          showError('Your reservation hold has expired. Please select your tickets again.');
          return;
        }
        if (err.errorCode === 'AlreadyConfirmed') {
          sessionStorage.removeItem('sp_hold');
          if (lastBookingReference) {
            window.location.href = `booking.html?ref=${encodeURIComponent(lastBookingReference)}`;
            return;
          }
          resetToSelectPhase();
          showError('This reservation has already been confirmed.');
          return;
        }
        if (err.errorCode === 'InvalidOwner') {
          if (typeof window.showToast === 'function') {
            window.showToast('Email does not match the reservation holder.', 'bad');
          }
          return;
        }
      }
      if (typeof window.showToast === 'function') {
        window.showToast(err.message || 'Could not confirm booking.', 'bad');
      }
    } finally {
      if (confirmBtn) {
        confirmBtn.disabled = false;
        confirmBtn.textContent = '✓ Confirm & Book';
      }
    }
  }

  document.addEventListener('DOMContentLoaded', async () => {
    const savedHold = (() => {
      try { return JSON.parse(sessionStorage.getItem('sp_hold')); } catch { return null; }
    })();

    // Check if the saved hold matches the current performance
    const currentPerfId = new URLSearchParams(location.search).get('id');
    if (savedHold &&
        savedHold.performanceId === currentPerfId &&
        parseUtcDate(savedHold.expiresAt) > new Date())
    {
      // Restore hold state WITHOUT re-fetching the API
      currentHold = savedHold;
      idempotencyKey = savedHold.idempotencyKey;
      quantity = savedHold.quantity || 1;
    }

    // Pre-fill customer email if logged in
    if (window.Auth && window.Auth.isLoggedIn()) {
      const user = window.Auth.getUser();
      if (user && user.email && customerEmailInput) {
        customerEmailInput.value = user.email;
      }
    }

    const params = new URLSearchParams(window.location.search);
    const id = params.get('id');
    if (!id) {
      if (perfTitle) perfTitle.textContent = 'Performance Not Found';
      showError('No performance ID was provided.');
      if (reserveBtn) reserveBtn.disabled = true;
      return;
    }

    try {
      const perf = await window.apiFetch(`/api/performances/${encodeURIComponent(id)}`);
      currentPerformance = perf;
      renderHero(perf);
      renderDetails(perf);
      renderTiers(perf);

      if (currentHold && parseUtcDate(currentHold.expiresAt) > new Date()) {
        // Move step indicator to done/active state
        markStep(1, 'done');
        markStep(2, 'done');
        markStep(3, 'active');
        // Populate hold summary
        document.getElementById('hold-summary-text').textContent =
          `${currentHold.quantity} × ${currentHold.ticketTypeName}`;
        // Hide the reservation form, show hold panel
        document.getElementById('phase-select').classList.add('hidden');
        document.getElementById('phase-hold').classList.remove('hidden');
        // Restore email field
        if (document.getElementById('customer-email'))
          document.getElementById('customer-email').value = currentHold.customerEmail || '';
        // Start countdown from expiresAt
        startCountdown(parseUtcDate(currentHold.expiresAt));
        // Show a toast
        window.showToast('Your hold has been restored. Confirm before it expires.', 'info', 5000);
      }
    } catch (err) {
      if (perfTitle) perfTitle.textContent = 'Performance Not Found';
      if (perfCat) perfCat.textContent = 'Unavailable';
      showError(err.status === 404 ? 'This performance does not exist or is no longer upcoming.' : (err.message || 'Unable to load performance.'));
      if (reserveBtn) reserveBtn.disabled = true;
    }

    if (qtyDecBtn) {
      qtyDecBtn.addEventListener('click', () => {
        if (quantity > 1) {
          quantity -= 1;
          updateStepperAndPrice();
        }
      });
    }

    if (qtyIncBtn) {
      qtyIncBtn.addEventListener('click', () => {
        const maxQty = selectedTier ? Math.min(10, selectedTier.available) : 1;
        if (quantity < maxQty) {
          quantity += 1;
          updateStepperAndPrice();
        }
      });
    }

    if (reserveBtn) {
      reserveBtn.addEventListener('click', () => submitReservation(false));
    }

    if (retryBtn) {
      retryBtn.addEventListener('click', () => submitReservation(true));
    }

    if (confirmBtn) {
      confirmBtn.addEventListener('click', confirmHold);
    }
  });
})();
