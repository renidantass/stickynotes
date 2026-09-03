(() => {
  'use strict';

  /* ---------- entrada do hero (escalonada, só se JS roda) ---------- */
  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const hero = document.querySelector('.hero');
  if (hero && !prefersReducedMotion) {
    // 1º frame: .js-hero aplica o estado inicial (opacity:0)
    // 2º frame: .hero-done transiciona para o estado final (opacity:1, escalonado)
    requestAnimationFrame(() => {
      document.body.classList.add('js-hero');
      requestAnimationFrame(() => {
        document.body.classList.add('hero-done');
      });
    });
  } else if (hero) {
    document.body.classList.add('js-hero', 'hero-done');
  }

  /* ---------- menu mobile ---------- */
  const toggle = document.getElementById('navToggle');
  const navLinks = document.getElementById('navLinks');
  const navCta = document.getElementById('navCta');

  const closeNav = () => {
    if (!navLinks) return;
    navLinks.classList.remove('open');
    navCta?.classList.remove('open');
    toggle?.setAttribute('aria-expanded', 'false');
  };

  toggle?.addEventListener('click', () => {
    const open = navLinks.classList.toggle('open');
    navCta?.classList.toggle('open', open);
    toggle.setAttribute('aria-expanded', String(open));
  });
  navLinks?.addEventListener('click', (e) => {
    if (e.target.closest('a')) closeNav();
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') closeNav();
  });

  /* ---------- tablist do tour ---------- */
  const tablist = document.querySelector('[role="tablist"]');
  const tabs = tablist ? [...tablist.querySelectorAll('[role="tab"]')] : [];

  const selectTab = (tab, opts = {}) => {
    const targetId = tab.getAttribute('aria-controls');
    const panel = document.getElementById(targetId);
    tabs.forEach((t) => {
      const active = t === tab;
      t.classList.toggle('active', active);
      t.setAttribute('aria-selected', String(active));
      t.tabIndex = active ? 0 : -1;
    });
    document.querySelectorAll('[role="tabpanel"]').forEach((p) => {
      const show = p.id === targetId;
      p.hidden = !show;
      p.classList.toggle('active', show);
    });
    if (!opts.keepFocus && tab === document.activeElement) {
      panel?.focus({ preventScroll: true });
    }
  };

  const onTabKey = (e) => {
    const idx = tabs.indexOf(document.activeElement);
    if (idx < 0) return;
    const last = tabs.length - 1;
    const move = {
      ArrowLeft: idx === 0 ? last : idx - 1,
      ArrowRight: idx === last ? 0 : idx + 1,
      Home: 0,
      End: last
    };
    if (!(e.key in move)) return;
    e.preventDefault();
    const next = tabs[move[e.key]];
    next.focus();
    selectTab(next, { keepFocus: true });
  };
  tablist?.addEventListener('keydown', onTabKey);
  tabs.forEach((tab) => {
    tab.addEventListener('click', () => selectTab(tab));
  });

  /* ---------- reveal on scroll (gated por reduced-motion) ---------- */
  const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const reveals = [...document.querySelectorAll('.reveal')];
  if (prefersReduced || !('IntersectionObserver' in window)) {
    reveals.forEach((el) => el.classList.add('visible'));
  } else {
    const io = new IntersectionObserver((entries) => {
      entries.forEach((en) => {
        if (en.isIntersecting) {
          en.target.classList.add('visible');
          io.unobserve(en.target);
        }
      });
    }, { threshold: 0.12 });
    reveals.forEach((el) => io.observe(el));
  }

  /* ---------- ano do rodapé ---------- */
  const year = document.getElementById('year');
  if (year) year.textContent = new Date().getFullYear();
})();
