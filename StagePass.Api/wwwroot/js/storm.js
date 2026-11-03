(function () {
  'use strict';

  const canvas  = document.getElementById('storm-canvas');
  const idleMsg = document.getElementById('storm-idle');
  if (!canvas) return;
  const ctx = canvas.getContext('2d');

  // ── Sizing & High-DPI Scaling ───────────────────────
  function resize() {
    const rect = canvas.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width  = Math.floor(rect.width  * dpr);
    canvas.height = Math.floor(rect.height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }
  window.addEventListener('resize', resize);
  if (typeof ResizeObserver !== 'undefined') {
    new ResizeObserver(() => resize()).observe(canvas);
  }
  resize();

  function W() { return canvas.getBoundingClientRect().width; }
  function H() { return canvas.getBoundingClientRect().height; }

  // ── Particle Pool ───────────────────────────────────
  // Each particle: { sx, sy, x, y, tx, ty, cx, cy, progress, speed, result, radius, alpha, done }
  // result: null (in flight) | 'ok' | 'conflict' | 'error'
  let particles = [];
  let running   = false;
  let animId    = null;

  const COLORS = {
    flight:   'rgba(255,255,255,0.25)',
    ok:       '#10b981',
    conflict: '#f59e0b',
    error:    '#ef4444',
  };

  // Server icon area: right ~15% of canvas
  function serverX() { return W() * 0.82; }
  function serverY() { return H() / 2; }

  // User spawn area: left ~10%
  function spawnX() { return W() * 0.05 + Math.random() * W() * 0.06; }
  function spawnY() { return H() * 0.2  + Math.random() * H() * 0.6;  }

  function addParticle() {
    // target is near the server with slight scatter
    const tx = serverX() + (Math.random() - 0.5) * 20;
    const ty = serverY() + (Math.random() - 0.5) * 40;
    const sx = spawnX();
    const sy = spawnY();
    particles.push({
      sx,
      sy,
      x:        sx,
      y:        sy,
      tx,
      ty,
      progress: 0,
      speed:    0.018 + Math.random() * 0.022,  // 0→1 per frame
      result:   null,
      radius:   2.5,
      alpha:    1,
      done:     false,
      // control point for arc (mid-flight upward bulge)
      cx: W() * 0.45 + (Math.random() - 0.5) * W() * 0.1,
      cy: H() * 0.1  + Math.random() * H() * 0.15,
    });
  }

  // Resolve the Nth unresolved particle with a result
  let resolveQueue = [];
  function resolveNext(result) {
    resolveQueue.push(result);
  }

  // Draw the server block icon
  function drawServer(cx, cy) {
    const w = 52, h = 36, r = 5;
    ctx.save();
    ctx.strokeStyle = 'rgba(124,58,237,0.6)';
    ctx.fillStyle   = 'rgba(124,58,237,0.15)';
    ctx.lineWidth   = 1.5;
    ctx.beginPath();
    if (typeof ctx.roundRect === 'function') {
      ctx.roundRect(cx - w / 2, cy - h / 2, w, h, r);
    } else {
      ctx.rect(cx - w / 2, cy - h / 2, w, h);
    }
    ctx.fill();
    ctx.stroke();

    // LED dots
    const ledColors = ['#10b981', '#f59e0b', 'rgba(255,255,255,0.3)'];
    ledColors.forEach((c, i) => {
      ctx.beginPath();
      ctx.arc(cx - w / 2 + 8, cy - h / 2 + 8 + i * 9, 2.5, 0, Math.PI * 2);
      ctx.fillStyle = c;
      ctx.fill();
    });

    // Label
    ctx.fillStyle = 'rgba(160,160,184,0.8)';
    ctx.font = '8px system-ui, -apple-system, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('SQL SERVER', cx, cy + h / 2 + 12);
    ctx.restore();
  }

  // Draw left "users" zone
  function drawUsersZone() {
    ctx.save();
    ctx.fillStyle = 'rgba(255,255,255,0.04)';
    ctx.fillRect(0, 0, W() * 0.12, H());
    ctx.fillStyle = 'rgba(160,160,184,0.35)';
    ctx.font = '9px system-ui, -apple-system, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('USERS', W() * 0.06, H() - 10);
    ctx.restore();
  }

  // Quadratic bezier interpolation
  function quadBezier(t, p0, p1, p2) {
    const mt = 1 - t;
    return mt * mt * p0 + 2 * mt * t * p1 + t * t * p2;
  }

  // Main animation loop
  function frame() {
    if (!running && particles.length === 0) {
      animId = null;
      if (idleMsg) idleMsg.style.display = 'flex';
      return;
    }

    // Clear
    ctx.clearRect(0, 0, W(), H());

    // Background gradient
    const grad = ctx.createLinearGradient(0, 0, W(), 0);
    grad.addColorStop(0,   'rgba(124,58,237,0.04)');
    grad.addColorStop(0.5, 'rgba(0,0,0,0)');
    grad.addColorStop(1,   'rgba(124,58,237,0.08)');
    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, W(), H());

    drawUsersZone();
    drawServer(serverX(), serverY());

    // Assign queued results to particles
    let resolvesThisFrame = 0;
    const maxResolves = Math.max(3, Math.ceil(resolveQueue.length / 8));
    while (resolveQueue.length > 0 && resolvesThisFrame < maxResolves) {
      const result = resolveQueue.shift();
      // Prefer first particle that has arrived or is near arrival (progress > 0.82)
      const target = particles.find(p => !p.done && p.result === null && p.progress > 0.82);
      if (target) {
        target.result = result;
        resolvesThisFrame++;
      } else {
        // Fall back to any unresolved in-flight particle
        const any = particles.find(p => !p.done && p.result === null);
        if (any) {
          any.result = result;
          resolvesThisFrame++;
        } else {
          // No particles available yet; put back in queue and wait
          resolveQueue.unshift(result);
          break;
        }
      }
    }

    // Update and draw particles
    particles = particles.filter(p => p.alpha > 0.01);
    for (const p of particles) {
      if (p.done) {
        // Fade out after explosion
        p.alpha -= 0.04;
        p.radius += 0.3;
        const col = COLORS[p.result] || COLORS.ok;
        ctx.save();
        ctx.globalAlpha = Math.max(0, p.alpha);
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2);
        ctx.fillStyle = col;
        ctx.shadowColor = col;
        ctx.shadowBlur = p.radius * 2;
        ctx.fill();
        ctx.restore();
        continue;
      }

      // Advance along arc
      p.progress += p.speed;

      if (p.progress >= 1) {
        // Arrived at server — resolve with result or stay at server
        if (p.result !== null) {
          p.x = p.tx;
          p.y = p.ty;
          p.done = true;
        } else {
          // Hover at server waiting for result
          p.progress = 0.98;
        }
      } else {
        p.x = quadBezier(p.progress, p.sx, p.cx, p.tx);
        p.y = quadBezier(p.progress, p.sy, p.cy, p.ty);
      }

      // Color based on state
      const isArriving = p.progress > 0.92;
      let color, glow;
      if (p.result !== null && isArriving) {
        color = COLORS[p.result];
        glow  = color;
      } else {
        color = COLORS.flight;
        glow  = 'rgba(255,255,255,0.2)';
      }

      ctx.save();
      ctx.globalAlpha = p.alpha;
      ctx.beginPath();
      ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2);
      ctx.fillStyle = color;
      ctx.shadowColor = glow;
      ctx.shadowBlur = 6;
      ctx.fill();
      ctx.restore();
    }

    // Draw connection lines between user zone and server (subtle)
    ctx.save();
    ctx.strokeStyle = 'rgba(124,58,237,0.06)';
    ctx.lineWidth = 1;
    ctx.setLineDash([3, 8]);
    ctx.beginPath();
    ctx.moveTo(W() * 0.12, H() / 2);
    ctx.lineTo(serverX() - 26, H() / 2);
    ctx.stroke();
    ctx.restore();

    animId = requestAnimationFrame(frame);
  }

  // ── Public API ──────────────────────────────────────
  window.Storm = {
    start(totalRequests) {
      running = true;
      particles = [];
      resolveQueue = [];
      if (idleMsg) idleMsg.style.display = 'none';
      if (!animId) animId = requestAnimationFrame(frame);
    },

    addRequest() {
      if (!running) return;
      addParticle();
    },

    resolveRequest(status) {
      if (!running) return;
      let result;
      if (status === 201 || status === 200) result = 'ok';
      else if (status === 409)              result = 'conflict';
      else                                  result = 'error';
      resolveNext(result);
    },

    stop() {
      running = false;
      // Let remaining particles fade out naturally
      setTimeout(() => {
        if (particles.length === 0 && idleMsg) {
          idleMsg.style.display = 'flex';
        }
      }, 2000);
    },
  };
})();
