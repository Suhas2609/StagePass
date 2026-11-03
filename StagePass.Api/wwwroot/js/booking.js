(function () {
  'use strict';

  const bookingContainer = document.getElementById('booking-container');

  function parseUtcDate(value) {
    const str = String(value);
    return new Date(/(Z|[+-]\d{2}:?\d{2})$/i.test(str) ? str : str + 'Z');
  }

  function formatDateTime(value) {
    if (!value) return '—';
    return parseUtcDate(value).toLocaleString('en-GB', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  function copyReference(ref) {
    const fallbackCopy = () => {
      const temp = document.createElement('textarea');
      temp.value = ref;
      document.body.appendChild(temp);
      temp.select();
      document.execCommand('copy');
      document.body.removeChild(temp);
    };

    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(ref)
        .catch(() => fallbackCopy())
        .then(() => {
          if (typeof window.showToast === 'function') {
            window.showToast('Reference copied!', 'ok');
          }
        });
    } else {
      fallbackCopy();
      if (typeof window.showToast === 'function') {
        window.showToast('Reference copied!', 'ok');
      }
    }
  }

  function renderErrorCard(message) {
    if (!bookingContainer) return;
    bookingContainer.innerHTML = '';

    const card = document.createElement('div');
    card.className = 'confirm-card fade-up';

    const body = document.createElement('div');
    body.className = 'confirm-body text-center';
    body.style.padding = '48px 32px';

    const title = document.createElement('h1');
    title.style.fontSize = 'var(--f-2xl)';
    title.style.fontWeight = '800';
    title.style.marginBottom = 'var(--sp-3)';
    title.textContent = 'Booking Unavailable';

    const desc = document.createElement('p');
    desc.style.color = 'var(--text-mid)';
    desc.style.marginBottom = 'var(--sp-6)';
    desc.textContent = message;

    const backBtn = document.createElement('a');
    backBtn.href = 'index.html';
    backBtn.className = 'btn btn-brand btn-full btn-lg';
    backBtn.textContent = 'Browse Events';

    body.append(title, desc, backBtn);
    card.appendChild(body);
    bookingContainer.appendChild(card);
  }

  function createDetailRow(label, value, isPrice = false) {
    const row = document.createElement('div');
    row.className = 'detail-row';

    const lbl = document.createElement('span');
    lbl.className = 'lbl';
    lbl.textContent = label;

    const val = document.createElement('span');
    val.className = isPrice ? 'val price' : 'val';
    val.textContent = value;

    row.append(lbl, val);
    return row;
  }

  function renderBooking(booking) {
    if (!bookingContainer) return;
    bookingContainer.innerHTML = '';

    const card = document.createElement('div');
    card.className = 'confirm-card fade-up';

    // Top Header
    const top = document.createElement('div');
    top.className = 'confirm-top';

    const check = document.createElement('div');
    check.className = 'confirm-check';
    check.textContent = '✓';

    const heading = document.createElement('h1');
    heading.textContent = 'Booking confirmed!';

    const subtitle = document.createElement('p');
    subtitle.textContent = 'Your tickets are secured and ready for the event.';

    top.append(check, heading, subtitle);

    // Body
    const body = document.createElement('div');
    body.className = 'confirm-body';

    // Clickable Reference Display
    const refDisplay = document.createElement('div');
    refDisplay.className = 'ref-display';
    refDisplay.title = 'Click to copy booking reference';

    const refLabel = document.createElement('div');
    refLabel.className = 'ref-label';
    refLabel.textContent = 'Booking Reference';

    const refCode = document.createElement('div');
    refCode.className = 'ref-code';
    refCode.textContent = booking.bookingReference;

    const refHint = document.createElement('div');
    refHint.className = 'ref-hint';
    refHint.textContent = 'Click to copy reference';

    refDisplay.append(refLabel, refCode, refHint);
    refDisplay.addEventListener('click', () => copyReference(booking.bookingReference));

    // Venue Note
    const venueNote = document.createElement('div');
    venueNote.className = 'venue-note';
    venueNote.textContent = 'Please present this reference at the venue entrance.';

    let emailNote = null;
    if (booking.customerIdentifier?.includes('@')) {
      emailNote = document.createElement('p');
      emailNote.style.cssText = 'font-size:var(--f-xs);color:var(--text-ghost);text-align:center;margin-top:var(--sp-3);margin-bottom:var(--sp-4);';
      emailNote.textContent = 'A confirmation email has been sent to your inbox.';
    }

    // Detail Rows
    const detailRows = document.createElement('div');
    detailRows.className = 'detail-rows';

    detailRows.appendChild(createDetailRow('Event', booking.eventTitle || '—'));
    detailRows.appendChild(createDetailRow('Date / Time', formatDateTime(booking.performanceStartDateTime)));
    detailRows.appendChild(createDetailRow('Venue', booking.venueName || '—'));
    detailRows.appendChild(createDetailRow('Ticket Type', booking.ticketTypeName || '—'));
    detailRows.appendChild(createDetailRow('Quantity', String(booking.quantity)));
    detailRows.appendChild(createDetailRow('Total Price', `£${Number(booking.totalPrice || 0).toFixed(2)}`, true));

    // Actions
    const actions = document.createElement('div');
    actions.className = 'confirm-actions';

    const browseBtn = document.createElement('a');
    browseBtn.href = 'index.html';
    browseBtn.className = 'btn btn-brand btn-full btn-lg';
    browseBtn.textContent = 'Browse More Events';

    const copyBtn = document.createElement('button');
    copyBtn.type = 'button';
    copyBtn.className = 'btn btn-outline btn-full';
    copyBtn.textContent = 'Copy Reference';
    copyBtn.addEventListener('click', () => copyReference(booking.bookingReference));

    actions.append(browseBtn, copyBtn);

    body.append(refDisplay, venueNote);
    if (emailNote) {
      body.appendChild(emailNote);
    }
    body.append(detailRows, actions);
    card.append(top, body);
    bookingContainer.appendChild(card);
  }

  document.addEventListener('DOMContentLoaded', async () => {
    const params = new URLSearchParams(window.location.search);
    const ref = params.get('ref');
    if (!ref || !ref.trim()) {
      renderErrorCard('No booking reference was provided in the URL.');
      return;
    }

    try {
      const booking = await window.apiFetch(`/api/bookings/${encodeURIComponent(ref.trim())}`);
      renderBooking(booking);
    } catch (err) {
      if (err.status === 404) {
        renderErrorCard('Booking not found. Please check your reference number and try again.');
      } else {
        renderErrorCard(err.message || 'Unable to load booking details.');
      }
    }
  });
})();
