(function () {
  'use strict';

  const bookingDetail = document.getElementById('booking-detail');
  const errorDisplay = document.getElementById('error-display');

  function formatDateTime(value) {
    const str = String(value);
    const d = new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(str) ? str : str + 'Z');
    return d.toLocaleString('en-GB', {
      dateStyle: 'full',
      timeStyle: 'short'
    });
  }

  function renderError(message) {
    if (errorDisplay) {
      errorDisplay.textContent = message;
    }
    bookingDetail.innerHTML = '';
    const wrapper = document.createElement('div');
    wrapper.style.textAlign = 'center';
    wrapper.style.padding = '60px 0';

    const p = document.createElement('p');
    p.style.color = 'var(--text-secondary)';
    p.textContent = message;

    const backLink = document.createElement('a');
    backLink.className = 'btn btn-secondary';
    backLink.href = 'index.html';
    backLink.style.marginTop = 'var(--space-lg)';
    backLink.textContent = '← Return to All Events';

    wrapper.append(p, backLink);
    bookingDetail.appendChild(wrapper);
  }

  function createDetailRow(label, value, isHighlight) {
    const row = document.createElement('div');
    row.className = 'detail-row';

    const labelSpan = document.createElement('span');
    labelSpan.className = 'label';
    labelSpan.textContent = label;

    const valueSpan = document.createElement('span');
    valueSpan.className = 'value';
    if (isHighlight) {
      valueSpan.style.color = 'var(--brand)';
    }
    valueSpan.textContent = value;

    row.append(labelSpan, valueSpan);
    return row;
  }

  function renderBooking(booking) {
    bookingDetail.innerHTML = '';
    if (errorDisplay) errorDisplay.textContent = '';

    const container = document.createElement('div');
    container.className = 'booking-success fade-in';

    // Check Icon
    const checkIcon = document.createElement('div');
    checkIcon.className = 'check-icon';
    checkIcon.textContent = '✓';

    // Headings
    const heading = document.createElement('h1');
    heading.textContent = 'Booking Confirmed!';

    const subtitle = document.createElement('p');
    subtitle.className = 'subtitle';
    subtitle.textContent = 'Your tickets are secured. Please save or present your booking reference upon arrival.';

    // Monospace Ref Box
    const refBox = document.createElement('div');
    refBox.className = 'ref-box';

    const refLabel = document.createElement('div');
    refLabel.className = 'ref-label';
    refLabel.textContent = 'Booking Reference';

    const refCode = document.createElement('div');
    refCode.className = 'ref-code';
    refCode.textContent = booking.bookingReference;

    const copyBtnWrapper = document.createElement('div');
    copyBtnWrapper.style.marginTop = 'var(--space-md)';

    const copyBtn = document.createElement('button');
    copyBtn.type = 'button';
    copyBtn.className = 'btn btn-secondary';
    copyBtn.style.fontSize = '0.85rem';
    copyBtn.style.padding = '8px 18px';
    copyBtn.textContent = '📋 Copy Reference';

    copyBtn.addEventListener('click', async (e) => {
      e.stopPropagation();
      try {
        await navigator.clipboard.writeText(booking.bookingReference);
        copyBtn.textContent = '✓ Copied!';
        setTimeout(() => {
          copyBtn.textContent = '📋 Copy Reference';
        }, 2000);
      } catch {
        // Fallback if clipboard API is blocked
        const temp = document.createElement('textarea');
        temp.value = booking.bookingReference;
        document.body.appendChild(temp);
        temp.select();
        document.execCommand('copy');
        document.body.removeChild(temp);
        copyBtn.textContent = '✓ Copied!';
        setTimeout(() => {
          copyBtn.textContent = '📋 Copy Reference';
        }, 2000);
      }
    });

    copyBtnWrapper.appendChild(copyBtn);
    refBox.append(refLabel, refCode, copyBtnWrapper);

    // Detail Card
    const detailCard = document.createElement('div');
    detailCard.className = 'booking-detail-card';

    detailCard.appendChild(createDetailRow('Event', booking.eventTitle || '—'));
    detailCard.appendChild(createDetailRow('Date', formatDateTime(booking.performanceStartDateTime)));
    detailCard.appendChild(createDetailRow('Venue', booking.venueName || '—'));
    detailCard.appendChild(createDetailRow('Ticket Type', booking.ticketTypeName || '—'));
    detailCard.appendChild(createDetailRow('Quantity', String(booking.quantity)));
    detailCard.appendChild(createDetailRow('Total Price', `£${Number(booking.totalPrice).toFixed(2)}`, true));

    // Return Action
    const actions = document.createElement('div');
    actions.style.display = 'flex';
    actions.style.justifyContent = 'center';
    actions.style.gap = 'var(--space-md)';
    actions.style.marginTop = 'var(--space-lg)';

    const backBtn = document.createElement('a');
    backBtn.className = 'btn btn-secondary btn-lg';
    backBtn.href = 'index.html';
    backBtn.textContent = 'Browse More Events';
    actions.appendChild(backBtn);

    container.append(checkIcon, heading, subtitle, refBox, detailCard, actions);
    bookingDetail.appendChild(container);
  }

  document.addEventListener('DOMContentLoaded', async () => {
    const params = new URLSearchParams(window.location.search);
    const ref = params.get('ref');
    if (!ref || !ref.trim()) {
      renderError('No booking reference provided in URL.');
      return;
    }

    try {
      const booking = await window.apiFetch(`/api/bookings/${encodeURIComponent(ref.trim())}`);
      renderBooking(booking);
      if (typeof window.showToast === 'function') {
        window.showToast('Booking confirmed!', 'success');
      }
    } catch (err) {
      if (err.status === 404) {
        renderError('Booking not found. Please verify your reference number.');
      } else {
        renderError(err.message || 'Unable to load booking details.');
      }
    }
  });
})();
