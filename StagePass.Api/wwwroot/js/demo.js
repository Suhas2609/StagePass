(function () {
  'use strict';

  async function runPool(taskFactories, poolSize, onEachResult, onTaskStart) {
    /*
      taskFactories: Array of () => Promise<Response>
      poolSize: max concurrent requests in flight
      onEachResult: callback(result, completedSoFar, totalCount)
                    called immediately when each task resolves.
                    result = { status: number, ok: boolean }
      onTaskStart: callback(index)
                    called when a task starts running in the pool
    */
    return new Promise((resolve) => {
      const total   = taskFactories.length;
      if (total === 0) {
        resolve([]);
        return;
      }
      const results = new Array(total);
      let nextIndex = 0;   // next task to launch
      let completed = 0;   // tasks that have resolved
      let active    = 0;   // tasks currently in flight

      function launch() {
        while (active < poolSize && nextIndex < total) {
          const i = nextIndex++;
          active++;
          onTaskStart && onTaskStart(i);
          taskFactories[i]()
            .then(r  => ({ status: r.status,  ok: true  }))
            .catch(() => ({ status: 0,         ok: false }))
            .then(result => {
              results[i] = result;
              active--;
              completed++;
              onEachResult(result, completed, total);
              if (completed === total) resolve(results);
              else launch();
            });
        }
      }

      launch();
    });
  }

  let runState = {
    total:    0,
    success:  0,   // 201 Created
    conflict: 0,   // 409 Conflict
    rateHit:  0,   // 429 Too Many Requests
    errors:   0,   // everything else (network errors, 5xx, etc.)
    startTime: 0,
  };

  function setNum(id, val) {
    const el = document.getElementById(id);
    if (el) el.textContent = val === null ? '—' : String(val);
  }

  function updateBars() {
    const t = runState.total || 1;
    const ps = (runState.success  / t * 100).toFixed(1);
    const pc = (runState.conflict / t * 100).toFixed(1);
    const pr = (runState.rateHit  / t * 100).toFixed(1);
    const pe = (runState.errors   / t * 100).toFixed(1);

    // Success bar (green) from left
    const bs = document.getElementById('bar-success');
    if (bs) bs.style.width = ps + '%';

    // Conflict bar (amber) starts where success ends
    const bc = document.getElementById('bar-conflict');
    if (bc) { bc.style.left = ps + '%'; bc.style.width = pc + '%'; }

    // Rate-limit bar (orange) after conflict
    const br = document.getElementById('bar-ratelimit');
    if (br) {
      const left = (parseFloat(ps) + parseFloat(pc)).toFixed(1);
      br.style.left = left + '%';
      br.style.width = pr + '%';
    }

    // Error bar (red) last
    const be = document.getElementById('bar-errors');
    if (be) {
      const left = (parseFloat(ps) + parseFloat(pc) + parseFloat(pr)).toFixed(1);
      be.style.left = left + '%';
      be.style.width = pe + '%';
    }

    // Requests per second
    const elapsed = (Date.now() - runState.startTime) / 1000 || 0.001;
    const completed = runState.success + runState.conflict + runState.rateHit + runState.errors;
    const rps = Math.round(completed / elapsed);
    setNum('r-rps', rps);
  }

  function onEachResult(result) {
    if (result.status === 201 || result.status === 200) runState.success++;
    else if (result.status === 409) runState.conflict++;
    else if (result.status === 429) runState.rateHit++;
    else runState.errors++;

    // Update DOM live
    setNum('r-success',   runState.success.toLocaleString());
    setNum('r-conflict',  runState.conflict.toLocaleString());
    setNum('r-ratelimit', runState.rateHit.toLocaleString());
    setNum('r-errors',    runState.errors.toLocaleString());

    const done = runState.success + runState.conflict + runState.rateHit + runState.errors;
    setNum('r-done', done.toLocaleString());

    // Progress bar label
    const leftLabel = document.getElementById('progress-label-left');
    if (leftLabel) {
      leftLabel.textContent = `${done.toLocaleString()} / ${runState.total.toLocaleString()} completed`;
    }
    updateBars();
  }

  const btn = document.getElementById('demo-run-btn');

  async function runDemo() {
    const tickets    = parseInt(document.getElementById('cfg-tickets').value,  10) || 100;
    const requests   = parseInt(document.getElementById('cfg-requests').value, 10) || 300;
    const poolSize   = parseInt(document.getElementById('cfg-pool').value,     10) || 200;

    btn.disabled = true;
    btn.textContent = '⏳ Setting up scenario…';

    // Reset state
    runState = { total: requests, success: 0, conflict: 0, rateHit: 0, errors: 0, startTime: 0 };
    ['r-success','r-conflict','r-ratelimit','r-errors','r-done','r-rps'].forEach(id => setNum(id, 0));
    setNum('r-total', requests.toLocaleString());
    const progressLeft = document.getElementById('progress-label-left');
    if (progressLeft) progressLeft.textContent = 'Setting up scenario…';
    const progressRight = document.getElementById('progress-label-right');
    if (progressRight) {
      progressRight.textContent = `${requests.toLocaleString()} requests → ${tickets.toLocaleString()} tickets`;
    }

    // Reset invariant panel
    const ip = document.getElementById('invariant-panel');
    if (ip) ip.className = 'invariant-panel';
    const formulaEl = document.getElementById('invariant-formula');
    if (formulaEl) formulaEl.textContent = 'Available + Pending + Confirmed = Capacity';
    const resultEl = document.getElementById('invariant-result');
    if (resultEl) {
      resultEl.style.color = '';
      resultEl.textContent = 'Verifying after all requests complete…';
    }

    // Reset bars
    ['bar-success','bar-conflict','bar-ratelimit','bar-errors'].forEach(id => {
      const el = document.getElementById(id);
      if (el) { el.style.width = '0%'; el.style.left = '0%'; }
    });

    const statusEl = document.getElementById('demo-setup-status');

    // --- SETUP SCENARIO ---
    let ticketTypeId;
    try {
      if (statusEl) statusEl.textContent = 'Creating scenario on server…';
      const scenario = await window.apiFetch(
        `/api/demo/setup-scenario?tickets=${tickets}&scenarioName=` +
        encodeURIComponent(`Live Demo — ${tickets} tickets, ${requests} concurrent`),
        { method: 'POST' }
      );
      ticketTypeId = scenario.ticketTypeId;
      if (statusEl) {
        statusEl.textContent =
          `✓ Scenario ready · TicketType #${ticketTypeId} · ${tickets.toLocaleString()} tickets available`;
      }
      btn.textContent = `▶ Firing ${requests.toLocaleString()} requests (${poolSize} concurrent)…`;
    } catch (err) {
      if (statusEl) {
        statusEl.textContent = 'Setup failed: ' + err.message +
          ' — Is the server running in Development mode?';
      }
      btn.disabled = false;
      btn.textContent = '▶ Run Test';
      if (typeof window.showToast === 'function') {
        window.showToast('Demo setup failed.', 'bad', 5000);
      }
      return;
    }

    // --- BUILD TASK FACTORIES ---
    const taskFactories = Array.from({ length: requests }, (_, i) => {
      const body = JSON.stringify({
        ticketTypeId,
        quantity: 1,
        idempotencyKey:     crypto.randomUUID(),
        customerIdentifier: `demo-${i}@stagepass.local`,
      });
      return () => fetch('/api/reservations', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body,
      });
    });

    // --- FIRE ---
    runState.startTime = Date.now();
    window.Storm?.start(requests);
    await runPool(
      taskFactories,
      poolSize,
      (result) => {
        window.Storm?.resolveRequest(result.status);
        onEachResult(result);
      },
      () => window.Storm?.addRequest()
    );
    window.Storm?.stop();

    const elapsed = ((Date.now() - runState.startTime) / 1000).toFixed(1);
    if (statusEl) {
      statusEl.textContent =
        `Completed ${requests.toLocaleString()} requests in ${elapsed}s · ` +
        `Expected ${Math.min(tickets, requests).toLocaleString()} allocated, ` +
        `${Math.max(0, requests - Math.min(tickets, requests)).toLocaleString()} rejected · ` +
        `Got ${runState.success.toLocaleString()} allocated, ${runState.conflict.toLocaleString()} sold out, ` +
        (runState.rateHit ? `${runState.rateHit.toLocaleString()} rate-limited, ` : '') +
        `${runState.errors.toLocaleString()} errors`;
    }

    // --- VERIFY INVARIANT ---
    try {
      const snap = await window.apiFetch(`/api/demo/snapshot/${ticketTypeId}`);
      const iff = document.getElementById('invariant-formula');
      const ir  = document.getElementById('invariant-result');
      if (iff) iff.textContent = snap.formula;
      if (snap.invariantHolds) {
        if (ip) ip.className = 'invariant-panel pass';
        if (ir) { ir.style.color = 'var(--ok)'; ir.textContent = '✓ Invariant holds. The database was never oversold.'; }
        if (typeof window.showToast === 'function') {
          window.showToast('Test complete — invariant verified ✓', 'ok', 5000);
        }
      } else {
        if (ip) ip.className = 'invariant-panel fail';
        if (ir) { ir.style.color = 'var(--bad)'; ir.textContent = '✗ INVARIANT VIOLATED — this should not happen.'; }
        if (typeof window.showToast === 'function') {
          window.showToast('INVARIANT VIOLATION detected!', 'bad', 8000);
        }
      }
    } catch (e) {
      const ir = document.getElementById('invariant-result');
      if (ir) ir.textContent = 'Snapshot failed: ' + e.message;
    }

    btn.disabled = false;
    btn.textContent = '▶ Run Again';
  }

  document.getElementById('demo-run-btn')?.addEventListener('click', runDemo);
})();
