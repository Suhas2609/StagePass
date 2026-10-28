(function() {
  const btn = document.getElementById('run-btn');
  const statusEl = document.getElementById('setup-status');

  function setResult(id, val) {
    const el = document.getElementById(id);
    if (el) el.textContent = val;
  }

  function updateTrack(success, conflict, total) {
    const pctSuccess  = total > 0 ? (success / total * 100) : 0;
    const pctConflict = total > 0 ? (conflict / total * 100) : 0;
    const s = document.getElementById('track-success');
    const c = document.getElementById('track-conflict');
    if (s) s.style.width = pctSuccess + '%';
    if (c) { c.style.left = pctSuccess + '%'; c.style.width = pctConflict + '%'; }
  }

  async function runDemo() {
    const tickets  = parseInt(document.getElementById('cfg-tickets').value, 10)  || 100;
    const requests = parseInt(document.getElementById('cfg-requests').value, 10) || 300;

    btn.disabled = true;
    btn.className = 'btn btn-primary btn-lg running';
    btn.textContent = '⏳ Setting up scenario…';
    statusEl.textContent = '';

    // Reset display
    ['r-total','r-success','r-conflict','r-errors','r-transport'].forEach(id => setResult(id, '—'));
    updateTrack(0, 0, 1);
    document.getElementById('invariant-formula').textContent = 'Available + Pending + Confirmed = Capacity';
    document.getElementById('invariant-result').textContent = '';
    document.getElementById('invariant-box').className = 'invariant-box';

    let ticketTypeId;
    try {
      const scenario = await apiFetch('/api/demo/setup-scenario?tickets=' + tickets + '&scenarioName=' + encodeURIComponent('Live Demo — ' + tickets + ' tickets'), { method: 'POST' });
      ticketTypeId = scenario.ticketTypeId;
      statusEl.textContent = `Scenario ready: Performance ID ${scenario.performanceId}, Ticket Type ID ${ticketTypeId}, ${tickets} tickets available.`;
      btn.textContent = `▶ Firing ${requests} concurrent requests…`;
    } catch (e) {
      if (typeof showToast === 'function') {
        showToast('Demo setup failed: ' + e.message + '. Is the server running in Development mode?', 'error', 6000);
      }
      statusEl.textContent = 'Setup failed: ' + e.message;
      btn.disabled = false;
      btn.className = 'btn btn-primary btn-lg';
      btn.textContent = '▶ Run Concurrency Test';
      return;
    }

    // Fire all requests concurrently; catch per-request transport rejections so one failed
    // socket/network request never aborts the entire batch.
    const tasks = Array.from({ length: requests }, (_, i) => {
      const payload = JSON.stringify({
        ticketTypeId,
        quantity: 1,
        idempotencyKey: crypto.randomUUID(),
        customerIdentifier: `demo-user-${i}@stagepass.local`
      });
      return fetch('/api/reservations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: payload
      }).then(
        res => ({ ok: true, status: res.status }),
        err => ({ ok: false, status: null, error: err })
      );
    });

    setResult('r-total', requests);
    const responses = await Promise.all(tasks);

    let success = 0, conflict = 0, httpErrors = 0, transportFailures = 0;
    for (const r of responses) {
      if (!r.ok || r.status === null) transportFailures++;
      else if (r.status === 201 || r.status === 200) success++;
      else if (r.status === 409) conflict++;
      else httpErrors++;
    }

    setResult('r-success', success);
    setResult('r-conflict', conflict);
    setResult('r-errors', httpErrors);
    setResult('r-transport', transportFailures);
    updateTrack(success, conflict, requests);

    // Fetch snapshot to verify invariant
    try {
      const snap = await apiFetch('/api/demo/snapshot/' + ticketTypeId);
      const formula = document.getElementById('invariant-formula');
      const result  = document.getElementById('invariant-result');
      const box     = document.getElementById('invariant-box');
      formula.textContent = snap.formula;
      if (snap.invariantHolds) {
        box.className = 'invariant-box pass';
        result.style.color = 'var(--status-upcoming-text)';
        result.textContent = '✓ Invariant holds. No oversell occurred.';
        if (typeof showToast === 'function') {
          showToast('Concurrency test complete. Invariant verified ✓', 'success', 5000);
        }
      } else {
        box.className = 'invariant-box fail';
        result.style.color = 'var(--alert-error-text)';
        result.textContent = '✗ Invariant VIOLATED. This should not happen.';
        if (typeof showToast === 'function') {
          showToast('INVARIANT VIOLATION detected!', 'error', 8000);
        }
      }
    } catch (e) {
      document.getElementById('invariant-result').textContent = 'Could not verify invariant: ' + e.message;
    }

    // Final expectation check
    const expectedSuccess = Math.min(tickets, requests);
    const expectedConflict = Math.max(0, requests - expectedSuccess);
    statusEl.textContent = `Expected: ${expectedSuccess} allocated, ${expectedConflict} rejected. Got: ${success} allocated, ${conflict} rejected. HTTP errors: ${httpErrors}. Transport failures: ${transportFailures}.`;

    btn.disabled = false;
    btn.className = 'btn btn-primary btn-lg';
    btn.textContent = '▶ Run Again';
  }

  btn.addEventListener('click', runDemo);
})();
