(function () {
  'use strict';

  let debounceTimer = null;

  const hero = document.getElementById('hero');
  const heroEyebrow = document.getElementById('hero-eyebrow');
  const heroTitle = document.getElementById('hero-title');
  const heroMeta = document.getElementById('hero-meta');
  const heroActions = document.getElementById('hero-actions');
  const heroIndicators = document.getElementById('hero-indicators');

  const searchInput = document.getElementById('search');
  const categoryScroll = document.getElementById('category-scroll');
  const sectionCarousels = document.getElementById('section-carousels');
  const trackThisWeek = document.getElementById('track-this-week');
  const trackTrending = document.getElementById('track-trending');
  const sectionAllGrid = document.getElementById('section-all-grid');
  const eventsGrid = document.getElementById('events-grid');
  const gridTitle = document.getElementById('grid-title');
  const emptyState = document.getElementById('empty-state');

  function formatDate(isoString) {
    if (!isoString) return '';
    const d = new Date(isoString);
    return d.toLocaleDateString('en-GB', {
      weekday: 'short',
      day: 'numeric',
      month: 'short'
    });
  }

  function formatHeroDate(isoString) {
    if (!isoString) return '';
    const d = new Date(isoString);
    return d.toLocaleDateString('en-GB', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  function renderSkeletons(targetElement, count = 6) {
    targetElement.innerHTML = '';
    for (let i = 0; i < count; i++) {
      const card = document.createElement('div');
      card.className = 'skeleton-card';

      const poster = document.createElement('div');
      poster.className = 'skeleton skeleton-poster';

      const info = document.createElement('div');
      info.className = 'skeleton-info';

      const line1 = document.createElement('div');
      line1.className = 'skeleton';
      line1.style.height = '14px';
      line1.style.width = '50%';

      const line2 = document.createElement('div');
      line2.className = 'skeleton';
      line2.style.height = '14px';
      line2.style.width = '75%';

      const line3 = document.createElement('div');
      line3.className = 'skeleton';
      line3.style.height = '16px';
      line3.style.width = '40%';
      line3.style.marginTop = '4px';

      info.append(line1, line2, line3);
      card.append(poster, info);
      targetElement.appendChild(card);
    }
  }

  function renderCard(item, sizeClass = 'card-md') {
    const card = document.createElement('a');
    card.className = `event-card ${sizeClass}`;
    const cat = (item.category || 'default').toLowerCase();
    card.setAttribute('data-cat', cat);
    card.href = `performance.html?id=${encodeURIComponent(item.performanceId)}`;

    // Top Poster
    const poster = document.createElement('div');
    poster.className = 'card-poster';

    const catBadge = document.createElement('span');
    catBadge.className = 'card-cat';
    catBadge.textContent = item.category || 'Event';

    const titleOverlay = document.createElement('div');
    titleOverlay.className = 'card-title-overlay';

    const title = document.createElement('div');
    title.className = 'event-title';
    title.textContent = item.eventTitle || 'Untitled Event';

    titleOverlay.appendChild(title);
    poster.append(catBadge, titleOverlay);

    // Bottom Info Strip
    const info = document.createElement('div');
    info.className = 'card-info';

    const date = document.createElement('div');
    date.className = 'card-date';
    date.textContent = formatDate(item.startDateTime);

    const venue = document.createElement('div');
    venue.className = 'card-venue';
    const venueParts = [item.venueName, item.city].filter(Boolean);
    venue.textContent = venueParts.join(', ');

    const price = document.createElement('div');
    price.className = 'card-price';
    if (item.status === 'Cancelled') {
      price.textContent = 'Cancelled';
      price.style.color = 'var(--bad)';
    } else if (item.totalAvailable === 0) {
      price.textContent = 'Sold out';
      price.style.color = 'var(--text-low)';
    } else {
      const formattedPrice = Number(item.lowestPrice || 0).toFixed(2);
      price.textContent = `From £${formattedPrice}`;
    }

    info.append(date, venue, price);
    card.append(poster, info);
    return card;
  }

  async function loadFeaturedHero() {
    try {
      const results = await window.apiFetch('/api/performances?page=1&pageSize=1');
      const item = Array.isArray(results) && results.length > 0 ? results[0] : null;
      if (!item) {
        if (heroTitle) heroTitle.textContent = 'Discover Live Events';
        return;
      }

      const cat = (item.category || 'default').toLowerCase();
      if (hero) hero.setAttribute('data-cat', cat);

      if (heroEyebrow) {
        heroEyebrow.textContent = `Featured · ${item.category || 'Live'}`;
      }

      if (heroTitle) {
        heroTitle.textContent = item.eventTitle || 'Featured Event';
      }

      if (heroMeta) {
        heroMeta.innerHTML = '';

        const dateItem = document.createElement('div');
        dateItem.className = 'hero-meta-item';
        dateItem.append(document.createTextNode('🗓️ '), document.createElement('strong'));
        dateItem.querySelector('strong').textContent = formatHeroDate(item.startDateTime);

        const sep1 = document.createElement('div');
        sep1.className = 'hero-meta-sep';

        const venueItem = document.createElement('div');
        venueItem.className = 'hero-meta-item';
        const venueParts = [item.venueName, item.city].filter(Boolean);
        venueItem.append(document.createTextNode('📍 '), document.createElement('strong'));
        venueItem.querySelector('strong').textContent = venueParts.join(', ');

        const sep2 = document.createElement('div');
        sep2.className = 'hero-meta-sep';

        const priceItem = document.createElement('div');
        priceItem.className = 'hero-meta-item';
        priceItem.append(document.createTextNode('🎟️ '), document.createElement('strong'));
        const formattedPrice = Number(item.lowestPrice || 0).toFixed(2);
        priceItem.querySelector('strong').textContent = `From £${formattedPrice}`;

        heroMeta.append(dateItem, sep1, venueItem, sep2, priceItem);
      }

      if (heroActions) {
        heroActions.innerHTML = '';

        const getTicketsBtn = document.createElement('a');
        getTicketsBtn.className = 'btn btn-xl btn-brand';
        getTicketsBtn.href = `performance.html?id=${encodeURIComponent(item.performanceId)}`;
        getTicketsBtn.textContent = 'Get Tickets';

        const saveBtn = document.createElement('button');
        saveBtn.className = 'btn btn-xl btn-outline';
        saveBtn.textContent = '♡ Save';
        saveBtn.addEventListener('click', () => {
          if (typeof window.showToast === 'function') {
            window.showToast(`Saved "${item.eventTitle}" to favorites!`, 'ok');
          }
        });

        heroActions.append(getTicketsBtn, saveBtn);
      }

      if (heroIndicators) {
        heroIndicators.innerHTML = '';
        const dot1 = document.createElement('div');
        dot1.className = 'hero-dot active';
        const dot2 = document.createElement('div');
        dot2.className = 'hero-dot';
        const dot3 = document.createElement('div');
        dot3.className = 'hero-dot';
        heroIndicators.append(dot1, dot2, dot3);
      }
    } catch (err) {
      if (heroTitle) heroTitle.textContent = 'Discover Live Events';
    }
  }

  async function loadCarousels() {
    if (trackThisWeek) renderSkeletons(trackThisWeek, 6);
    if (trackTrending) renderSkeletons(trackTrending, 6);

    try {
      const results = await window.apiFetch('/api/performances?page=1&pageSize=12');
      const list = Array.isArray(results) ? results : [];

      // "This Week" (next 7 days, fallback to first 6 if none in window)
      if (trackThisWeek) {
        trackThisWeek.innerHTML = '';
        const now = new Date();
        const sevenDaysLater = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);
        let thisWeek = list.filter(p => {
          const d = new Date(p.startDateTime);
          return d >= now && d <= sevenDaysLater;
        });
        if (thisWeek.length === 0) {
          thisWeek = list.slice(0, 6);
        }

        if (thisWeek.length === 0) {
          const emptyMsg = document.createElement('p');
          emptyMsg.style.color = 'var(--text-low)';
          emptyMsg.style.padding = 'var(--sp-4)';
          emptyMsg.textContent = 'No performances scheduled for this week.';
          trackThisWeek.appendChild(emptyMsg);
        } else {
          thisWeek.forEach(item => {
            trackThisWeek.appendChild(renderCard(item, 'card-md'));
          });
        }
      }

      // "Trending Near You" (distinct or reversed subset)
      if (trackTrending) {
        trackTrending.innerHTML = '';
        const trending = [...list].reverse();
        if (trending.length === 0) {
          const emptyMsg = document.createElement('p');
          emptyMsg.style.color = 'var(--text-low)';
          emptyMsg.style.padding = 'var(--sp-4)';
          emptyMsg.textContent = 'No trending performances at the moment.';
          trackTrending.appendChild(emptyMsg);
        } else {
          trending.forEach(item => {
            trackTrending.appendChild(renderCard(item, 'card-md'));
          });
        }
      }
    } catch (err) {
      if (trackThisWeek) trackThisWeek.innerHTML = '';
      if (trackTrending) trackTrending.innerHTML = '';
    }
  }

  async function performSearch(term) {
    sectionCarousels.style.display = 'none';
    sectionAllGrid.style.display = 'block';
    if (gridTitle) gridTitle.textContent = `Search Results for "${term}"`;
    if (emptyState) emptyState.style.display = 'none';
    renderSkeletons(eventsGrid, 6);

    try {
      const results = await window.apiFetch(`/api/performances?search=${encodeURIComponent(term)}&page=1&pageSize=24`);
      const list = Array.isArray(results) ? results : [];
      eventsGrid.innerHTML = '';

      if (list.length === 0) {
        if (emptyState) emptyState.style.display = 'block';
      } else {
        if (emptyState) emptyState.style.display = 'none';
        list.forEach(item => {
          eventsGrid.appendChild(renderCard(item, 'card-md'));
        });
      }
    } catch (err) {
      eventsGrid.innerHTML = '';
      if (emptyState) emptyState.style.display = 'block';
    }
  }

  async function filterByCategory(cat, catName) {
    sectionCarousels.style.display = 'none';
    sectionAllGrid.style.display = 'block';
    if (gridTitle) gridTitle.textContent = `${catName} Events`;
    if (emptyState) emptyState.style.display = 'none';
    renderSkeletons(eventsGrid, 6);

    try {
      const results = await window.apiFetch(`/api/performances?category=${encodeURIComponent(cat)}&page=1&pageSize=24`);
      const list = Array.isArray(results) ? results : [];
      eventsGrid.innerHTML = '';

      if (list.length === 0) {
        if (emptyState) emptyState.style.display = 'block';
      } else {
        if (emptyState) emptyState.style.display = 'none';
        list.forEach(item => {
          eventsGrid.appendChild(renderCard(item, 'card-md'));
        });
      }
    } catch (err) {
      eventsGrid.innerHTML = '';
      if (emptyState) emptyState.style.display = 'block';
    }
  }

  function showCarousels() {
    sectionAllGrid.style.display = 'none';
    sectionCarousels.style.display = 'block';
    if (emptyState) emptyState.style.display = 'none';
  }

  function initCarouselNav() {
    document.querySelectorAll('.carousel-btn[data-dir][data-target]').forEach(btn => {
      btn.addEventListener('click', () => {
        const dir = parseInt(btn.dataset.dir, 10) || 1;
        const targetId = btn.dataset.target;
        const track = document.getElementById(targetId);
        if (track) {
          track.scrollBy({ left: dir * 300, behavior: 'smooth' });
        }
      });
    });

    const viewAllThisWeek = document.getElementById('view-all-this-week');
    if (viewAllThisWeek) {
      viewAllThisWeek.addEventListener('click', (e) => {
        e.preventDefault();
        filterByCategory('', 'All Upcoming');
      });
    }

    const viewAllTrending = document.getElementById('view-all-trending');
    if (viewAllTrending) {
      viewAllTrending.addEventListener('click', (e) => {
        e.preventDefault();
        filterByCategory('', 'All Upcoming');
      });
    }
  }

  function initCategoryPills() {
    if (!categoryScroll) return;

    categoryScroll.querySelectorAll('.cat-pill').forEach(pill => {
      pill.addEventListener('click', () => {
        categoryScroll.querySelectorAll('.cat-pill').forEach(p => p.classList.remove('active'));
        pill.classList.add('active');

        const displayCat = pill.dataset.cat;
        if (searchInput) searchInput.value = '';

        if (!displayCat) {
          showCarousels();
        } else {
          // Capitalize for the API query since DB stores "Music" not "music"
          const apiCat = displayCat.charAt(0).toUpperCase() + displayCat.slice(1);
          const catName = pill.textContent.replace(/^[^\w\s]+\s*/, '').trim();
          filterByCategory(apiCat, catName);
        }
      });
    });
  }

  function initSearch() {
    if (!searchInput) return;

    searchInput.addEventListener('input', () => {
      if (debounceTimer) clearTimeout(debounceTimer);

      debounceTimer = setTimeout(() => {
        const term = searchInput.value.trim();
        if (term.length > 0) {
          // Deactivate category pills
          if (categoryScroll) {
            categoryScroll.querySelectorAll('.cat-pill').forEach(p => p.classList.remove('active'));
          }
          performSearch(term);
        } else {
          // Reset to All Events
          if (categoryScroll) {
            const allPill = categoryScroll.querySelector('.cat-pill[data-cat=""]');
            if (allPill) allPill.classList.add('active');
          }
          showCarousels();
        }
      }, 300);
    });
  }

  document.addEventListener('DOMContentLoaded', () => {
    initCarouselNav();
    initCategoryPills();
    initSearch();
    loadFeaturedHero();
    loadCarousels();
  });
})();
