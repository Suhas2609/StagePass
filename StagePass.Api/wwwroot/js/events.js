(function () {
  'use strict';

  const PAGE_SIZE = 9;
  const knownCategories = new Set();
  let currentPage = 1;
  let debounceTimer = null;

  const searchInput = document.getElementById('search');
  const categorySelect = document.getElementById('category');
  const dateInput = document.getElementById('date');
  const searchBtn = document.getElementById('search-btn');
  const eventsGrid = document.getElementById('events-grid');
  const emptyState = document.getElementById('empty-state');
  const errorDisplay = document.getElementById('error-display');
  const prevBtn = document.getElementById('prev-btn');
  const nextBtn = document.getElementById('next-btn');
  const pageLabel = document.getElementById('page-label');

  function formatDateTime(isoString) {
    const normalized = /(Z|[+-]\d{2}:?\d{2})$/i.test(String(isoString))
      ? String(isoString)
      : String(isoString) + 'Z';
    const d = new Date(normalized);
    return d.toLocaleString('en-GB', {
      dateStyle: 'medium',
      timeStyle: 'short'
    });
  }

  function updateUrlParams(page) {
    const params = new URLSearchParams(window.location.search);
    params.set('page', String(page));
    if (searchInput.value.trim()) {
      params.set('search', searchInput.value.trim());
    } else {
      params.delete('search');
    }
    if (categorySelect.value) {
      params.set('category', categorySelect.value);
    } else {
      params.delete('category');
    }
    if (dateInput.value) {
      params.set('date', dateInput.value);
    } else {
      params.delete('date');
    }
    const newUrl = window.location.pathname + '?' + params.toString();
    window.history.replaceState(null, '', newUrl);
  }

  function populateCategoryDropdown(items) {
    const previousValue = categorySelect.value;
    for (const item of items) {
      if (item.category) {
        knownCategories.add(item.category);
      }
    }
    const sorted = Array.from(knownCategories).sort((a, b) => a.localeCompare(b));
    categorySelect.innerHTML = '';
    const allOption = document.createElement('option');
    allOption.value = '';
    allOption.textContent = 'All Categories';
    categorySelect.appendChild(allOption);

    for (const cat of sorted) {
      const opt = document.createElement('option');
      opt.value = cat;
      opt.textContent = cat;
      categorySelect.appendChild(opt);
    }
    if (previousValue && knownCategories.has(previousValue)) {
      categorySelect.value = previousValue;
    }
  }

  function renderSkeletons(count = 6) {
    eventsGrid.innerHTML = '';
    emptyState.hidden = true;
    for (let i = 0; i < count; i++) {
      const card = document.createElement('div');
      card.className = 'event-card';
      card.style.pointerEvents = 'none';
      card.innerHTML = `
        <div class="skeleton" style="height:1.2rem; width:70%; margin-bottom:12px;"></div>
        <div class="skeleton" style="height:0.85rem; width:45%; margin-bottom:8px;"></div>
        <div class="skeleton" style="height:0.85rem; width:60%; margin-bottom:8px;"></div>
        <div class="skeleton" style="height:0.85rem; width:55%; margin-bottom:24px;"></div>
        <div class="skeleton" style="height:36px; width:100%;"></div>
      `;
      eventsGrid.appendChild(card);
    }
  }

  function renderCards(items) {
    eventsGrid.innerHTML = '';
    if (!items || items.length === 0) {
      emptyState.hidden = false;
      return;
    }
    emptyState.hidden = true;

    items.forEach((item, index) => {
      const card = document.createElement('div');
      card.className = 'event-card fade-in';
      card.style.animationDelay = `${Math.min(index, 5) * 50}ms`;
      if (item.category) {
        card.setAttribute('data-category', item.category.toLowerCase());
      }

      // Card Header: Title + Badge
      const header = document.createElement('div');
      header.className = 'card-header';

      const title = document.createElement('h3');
      title.className = 'card-title';
      title.textContent = item.eventTitle;

      const badge = document.createElement('span');
      badge.className = 'badge';
      if (item.totalAvailable === 0) {
        badge.classList.add('badge-sold-out');
        badge.textContent = 'Sold Out';
      } else if (item.status === 'Cancelled') {
        badge.classList.add('badge-cancelled');
        badge.textContent = 'Cancelled';
      } else {
        badge.classList.add('badge-upcoming');
        badge.textContent = 'Upcoming';
      }

      header.append(title, badge);

      // Card Meta: Category, Venue, Date
      const meta = document.createElement('div');
      meta.className = 'card-meta';

      const catSpan = document.createElement('span');
      catSpan.textContent = item.category || 'General';

      const venueSpan = document.createElement('span');
      venueSpan.textContent = `${item.venueName}, ${item.city}`;

      const dateSpan = document.createElement('span');
      dateSpan.textContent = formatDateTime(item.startDateTime);

      meta.append(catSpan, venueSpan, dateSpan);

      // Card Footer: Price + View Tickets Link
      const footer = document.createElement('div');
      footer.className = 'card-footer';

      const price = document.createElement('div');
      price.className = 'card-price';
      const small = document.createElement('small');
      small.textContent = 'From ';
      const priceText = document.createTextNode(`£${Number(item.lowestPrice).toFixed(2)}`);
      price.append(small, priceText);

      const link = document.createElement('a');
      link.className = 'btn btn-primary btn-full';
      link.href = `performance.html?id=${encodeURIComponent(item.performanceId)}`;
      link.textContent = 'View Tickets';

      card.append(header, meta, footer, link);
      eventsGrid.appendChild(card);
    });
  }

  async function loadPerformances() {
    errorDisplay.textContent = '';
    renderSkeletons(6);
    eventsGrid.classList.add('loading');

    const query = new URLSearchParams();
    const searchTerm = searchInput.value.trim();
    if (searchTerm) {
      query.set('search', searchTerm);
    }
    if (categorySelect.value) {
      query.set('category', categorySelect.value);
    }
    if (dateInput.value) {
      query.set('date', dateInput.value);
    }
    query.set('page', String(currentPage));
    query.set('pageSize', String(PAGE_SIZE));

    try {
      const results = await window.apiFetch(`/api/performances?${query.toString()}`);
      const list = Array.isArray(results) ? results : [];
      populateCategoryDropdown(list);

      const filtered = searchTerm
        ? list.filter(p => p.eventTitle && p.eventTitle.toLowerCase().includes(searchTerm.toLowerCase()))
        : list;

      renderCards(filtered);
      pageLabel.textContent = `Page ${currentPage}`;
      prevBtn.disabled = currentPage <= 1;
      nextBtn.disabled = list.length < PAGE_SIZE;
    } catch (err) {
      eventsGrid.innerHTML = '';
      errorDisplay.textContent = err.message || 'Unable to load performances. Please try again.';
      emptyState.hidden = true;
    } finally {
      eventsGrid.classList.remove('loading');
    }
  }

  function scheduleFilterReload() {
    if (debounceTimer) {
      clearTimeout(debounceTimer);
    }
    debounceTimer = setTimeout(() => {
      currentPage = 1;
      updateUrlParams(currentPage);
      loadPerformances();
    }, 300);
  }

  document.addEventListener('DOMContentLoaded', () => {
    const params = new URLSearchParams(window.location.search);
    const parsedPage = parseInt(params.get('page') || '1', 10);
    currentPage = Number.isNaN(parsedPage) || parsedPage < 1 ? 1 : parsedPage;

    if (params.get('search')) {
      searchInput.value = params.get('search');
    }
    if (params.get('category')) {
      knownCategories.add(params.get('category'));
      populateCategoryDropdown([]);
      categorySelect.value = params.get('category');
    }
    if (params.get('date')) {
      dateInput.value = params.get('date');
    }

    searchInput.addEventListener('input', scheduleFilterReload);
    categorySelect.addEventListener('change', scheduleFilterReload);
    dateInput.addEventListener('change', scheduleFilterReload);

    searchBtn.addEventListener('click', () => {
      if (debounceTimer) {
        clearTimeout(debounceTimer);
      }
      currentPage = 1;
      updateUrlParams(currentPage);
      loadPerformances();
    });

    prevBtn.addEventListener('click', () => {
      if (currentPage > 1) {
        currentPage -= 1;
        updateUrlParams(currentPage);
        loadPerformances();
      }
    });

    nextBtn.addEventListener('click', () => {
      currentPage += 1;
      updateUrlParams(currentPage);
      loadPerformances();
    });

    loadPerformances();
  });
})();
