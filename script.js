/* Full Art Spire — showcase interactions (vanilla JS, zero dependencies) */

/**
 * Maintainer configuration: fill in these fields in one place once published.
 * - steamWorkshopUrl / nexusModsUrl: leave empty to show disabled "Coming soon" buttons.
 * - youtubeVideoId: optional 11-character YouTube video ID; leave empty to hide the Trailer section.
 */
const SITE_CONFIG = {
  steamWorkshopUrl: "",
  nexusModsUrl: "",
  youtubeVideoId: "",
};

(() => {
  const reducedMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
  const finePointerQuery = window.matchMedia("(hover: hover) and (pointer: fine)");
  const reduced = () => reducedMotionQuery.matches;

  // 1. Store buttons and optional YouTube trailer (privacy-enhanced youtube-nocookie.com)
  function applyStoreConfig() {
    const bindings = [
      { id: "btn-workshop", url: SITE_CONFIG.steamWorkshopUrl },
      { id: "btn-nexus", url: SITE_CONFIG.nexusModsUrl },
    ];

    for (const { id, url } of bindings) {
      const el = document.getElementById(id);
      if (!el) continue;
      const trimmed = typeof url === "string" ? url.trim() : "";
      const badge = el.querySelector(".btn-badge");
      if (/^https:\/\/.+/i.test(trimmed)) {
        el.href = trimmed;
        el.target = "_blank";
        el.rel = "noopener noreferrer";
        el.classList.remove("is-disabled");
        el.removeAttribute("aria-disabled");
        el.removeAttribute("tabindex");
        if (badge) badge.remove();
      } else {
        el.removeAttribute("href");
        el.classList.add("is-disabled");
        el.setAttribute("aria-disabled", "true");
        el.setAttribute("tabindex", "-1");
      }
    }

    const trailerSection = document.getElementById("trailer");
    const trailerHost = document.getElementById("trailer-embed");
    const vid = typeof SITE_CONFIG.youtubeVideoId === "string" ? SITE_CONFIG.youtubeVideoId.trim() : "";
    if (trailerSection && trailerHost && /^[A-Za-z0-9_-]{11}$/.test(vid)) {
      const iframe = document.createElement("iframe");
      iframe.src = `https://www.youtube-nocookie.com/embed/${vid}`;
      iframe.title = "Full Art Spire trailer";
      iframe.loading = "lazy";
      iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture";
      iframe.allowFullscreen = true;
      iframe.referrerPolicy = "strict-origin-when-cross-origin";
      trailerHost.replaceChildren(iframe);
      trailerSection.hidden = false;
    }
  }

  // 2. Light / dark toggle. The choice is a per-browser convenience; the page works without it.
  function initThemeToggle() {
    const btn = document.querySelector("[data-theme-toggle]");
    if (!btn) return;
    const root = document.documentElement;
    const systemDark = window.matchMedia("(prefers-color-scheme: dark)");
    const current = () => root.getAttribute("data-theme") || (systemDark.matches ? "dark" : "light");
    const label = () => btn.setAttribute("aria-label", current() === "dark" ? "Switch to light theme" : "Switch to dark theme");
    label();
    systemDark.addEventListener("change", label);
    btn.addEventListener("click", () => {
      const next = current() === "dark" ? "light" : "dark";
      root.setAttribute("data-theme", next);
      try { localStorage.setItem("fas-theme", next); } catch (e) { /* storage blocked: keep for this visit */ }
      label();
    });
  }

  // 3. Endless card ribbons: duplicate each track once so the -50% loop is seamless.
  function initRibbons() {
    for (const track of document.querySelectorAll("[data-ribbon]")) {
      for (const child of Array.from(track.children)) {
        const copy = child.cloneNode(true);
        copy.setAttribute("aria-hidden", "true");
        track.appendChild(copy);
      }
    }
  }

  // 4. Tilt + holographic glare on cards (fine pointers only; off under Reduced Motion).
  function initTilt() {
    const faces = document.querySelectorAll("[data-tilt]");
    for (const face of faces) {
      const host = face.closest("button") || face;
      const move = (e) => {
        if (reduced() || !finePointerQuery.matches) return;
        const r = face.getBoundingClientRect();
        if (!r.width) return;
        const x = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
        const y = Math.min(1, Math.max(0, (e.clientY - r.top) / r.height));
        face.classList.add("is-tilting");
        face.style.setProperty("--ry", `${((x - 0.5) * 22).toFixed(2)}deg`);
        face.style.setProperty("--rx", `${((0.5 - y) * 18).toFixed(2)}deg`);
        face.style.setProperty("--mx", `${(x * 100).toFixed(1)}%`);
        face.style.setProperty("--my", `${(y * 100).toFixed(1)}%`);
        face.style.setProperty("--glare", "1");
      };
      const leave = () => {
        face.classList.remove("is-tilting");
        face.style.setProperty("--rx", "0deg");
        face.style.setProperty("--ry", "0deg");
        face.style.setProperty("--glare", "0");
      };
      host.addEventListener("pointermove", move);
      host.addEventListener("pointerleave", leave);
    }
  }

  // 5. Accessible before / after sliders driven by native <input type="range">
  function initBeforeAfter() {
    for (const viewport of document.querySelectorAll("[data-ba]")) {
      const range = viewport.querySelector(".ba-range");
      if (!range) continue;
      const update = () => {
        const val = Math.min(100, Math.max(0, Number(range.value) || 0));
        viewport.style.setProperty("--pos", `${val}%`);
      };
      range.addEventListener("input", update);
      range.addEventListener("change", update);
      update();
    }
  }

  // 6. Browser parallax approximation from the shipped background + foreground layers
  function initDepthParallax() {
    const stages = document.querySelectorAll("[data-depth-stage]");
    const hints = document.querySelectorAll("[data-depth-hint]");

    function syncHintText() {
      for (const hint of hints) {
        hint.textContent = reduced()
          ? "Static view: Reduced Motion is on."
          : "Move your pointer over a card, or drag across it.";
      }
      if (reduced()) {
        for (const still of document.querySelectorAll(".depth-still[data-src]")) {
          still.src = still.dataset.src;
          still.removeAttribute("data-src");
        }
      }
    }
    syncHintText();
    reducedMotionQuery.addEventListener("change", syncHintText);

    for (const stage of stages) {
      let draggingTouch = false;

      const setOffset = (clientX, clientY) => {
        if (reduced()) return;
        const rect = stage.getBoundingClientRect();
        if (!rect.width || !rect.height) return;
        const nx = Math.max(-1, Math.min(1, ((clientX - rect.left) / rect.width) * 2 - 1));
        const ny = Math.max(-1, Math.min(1, ((clientY - rect.top) / rect.height) * 2 - 1));
        stage.style.setProperty("--px", nx.toFixed(4));
        stage.style.setProperty("--py", ny.toFixed(4));
        stage.style.setProperty("--active", "1");
      };

      const reset = () => {
        draggingTouch = false;
        stage.classList.remove("is-interacting");
        stage.style.setProperty("--px", "0");
        stage.style.setProperty("--py", "0");
        stage.style.setProperty("--active", "0");
      };

      stage.addEventListener("pointerenter", (e) => {
        if (e.pointerType === "mouse" && !reduced()) {
          stage.classList.add("is-interacting");
          setOffset(e.clientX, e.clientY);
        }
      });
      stage.addEventListener("pointermove", (e) => {
        if (reduced()) return;
        if (e.pointerType === "mouse" || draggingTouch) {
          stage.classList.add("is-interacting");
          setOffset(e.clientX, e.clientY);
        }
      });
      stage.addEventListener("pointerdown", (e) => {
        if (reduced()) return;
        if (e.pointerType !== "mouse") {
          draggingTouch = true;
          stage.classList.add("is-interacting");
          setOffset(e.clientX, e.clientY);
        }
      });
      stage.addEventListener("pointerup", reset);
      stage.addEventListener("pointercancel", reset);
      stage.addEventListener("pointerleave", reset);
    }
  }

  // 7. Upgrade the rarity finish loops to their animated versions only once scrolled into view
  function initDeferredLoops() {
    const imgs = Array.from(document.querySelectorAll("img[data-anim-src]"));
    if (!imgs.length) return;

    reducedMotionQuery.addEventListener("change", () => {
      for (const img of imgs) {
        if (reduced()) {
          if (img.dataset.stillSrc) img.src = img.dataset.stillSrc;
        } else if (img.dataset.activated === "true" && img.dataset.animSrc) {
          img.src = img.dataset.animSrc;
        }
      }
    });

    const activateImg = (img) => {
      img.dataset.activated = "true";
      if (!reduced() && img.dataset.animSrc) img.src = img.dataset.animSrc;
    };

    if ("IntersectionObserver" in window) {
      const io = new IntersectionObserver((entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            activateImg(entry.target);
            io.unobserve(entry.target);
          }
        }
      }, { rootMargin: "160px 0px" });
      for (const img of imgs) io.observe(img);
    } else {
      for (const img of imgs) activateImg(img);
    }
  }

  // 8. Accessible ARIA tabs for the gallery; the panel takes the pool's colour and deals its cards in
  function initTabs() {
    const tablist = document.querySelector("[data-gallery-tabs]");
    if (!tablist) return;
    const tabs = Array.from(tablist.querySelectorAll('[role="tab"]'));
    const galleryPanel = tablist.closest(".gallery-panel");

    function hydratePanel(panel) {
      for (const img of panel.querySelectorAll("img[data-src]")) {
        img.src = img.dataset.src;
        img.removeAttribute("data-src");
      }
    }

    function deal(panel) {
      if (reduced()) return;
      const grid = panel.querySelector(".card-grid");
      if (!grid) return;
      Array.from(grid.children).forEach((tile, i) => tile.style.setProperty("--i", String(i)));
      grid.classList.remove("is-dealing");
      void grid.offsetWidth; // restart the animation
      grid.classList.add("is-dealing");
    }

    function activate(tab, moveFocus) {
      for (const candidate of tabs) {
        const selected = candidate === tab;
        candidate.setAttribute("aria-selected", selected ? "true" : "false");
        candidate.tabIndex = selected ? 0 : -1;
        const panelId = candidate.getAttribute("aria-controls");
        const panel = panelId ? document.getElementById(panelId) : null;
        if (!panel) continue;
        panel.hidden = !selected;
        if (selected) {
          hydratePanel(panel);
          deal(panel);
        }
      }
      if (galleryPanel && tab.dataset.pool) galleryPanel.dataset.pool = tab.dataset.pool;
      if (moveFocus) tab.focus();
    }

    for (const tab of tabs) {
      tab.addEventListener("click", () => activate(tab, false));
      tab.addEventListener("keydown", (e) => {
        const idx = tabs.indexOf(tab);
        let next = -1;
        if (e.key === "ArrowRight") next = (idx + 1) % tabs.length;
        else if (e.key === "ArrowLeft") next = (idx - 1 + tabs.length) % tabs.length;
        else if (e.key === "Home") next = 0;
        else if (e.key === "End") next = tabs.length - 1;
        if (next >= 0) {
          e.preventDefault();
          activate(tabs[next], true);
        }
      });
    }
  }

  // 9. Accessible card lightbox (<dialog>) with previous / next navigation
  function initLightbox() {
    const dialog = document.getElementById("card-lightbox");
    if (!dialog) return;
    const imgEl = dialog.querySelector("#lightbox-img");
    const titleEl = dialog.querySelector("#lightbox-title");
    const subEl = dialog.querySelector("#lightbox-sub");
    const counterEl = dialog.querySelector("#lightbox-counter");
    const closeBtn = dialog.querySelector("#lightbox-close");
    const prevBtn = dialog.querySelector("#lightbox-prev");
    const nextBtn = dialog.querySelector("#lightbox-next");

    let group = [];
    let index = 0;
    let lastTrigger = null;

    function render() {
      const btn = group[index];
      if (!btn) return;
      const name = btn.dataset.name || "";
      const meta = btn.dataset.meta || "";
      const thumbAlt = btn.querySelector("img")?.alt;
      dialog.classList.toggle("is-wide", btn.dataset.wide === "true");
      titleEl.textContent = name;
      subEl.textContent = meta;
      imgEl.src = btn.dataset.full || "";
      imgEl.alt = thumbAlt || `${name} (${meta}) — full-art card`;
      counterEl.textContent = `${index + 1} / ${group.length}`;
      prevBtn.disabled = group.length <= 1;
      nextBtn.disabled = group.length <= 1;
    }

    function openFrom(trigger) {
      const scope = trigger.closest("[data-lightbox-group]") || document;
      group = Array.from(scope.querySelectorAll("[data-lightbox-item]"));
      index = Math.max(0, group.indexOf(trigger));
      lastTrigger = trigger;
      render();
      if (typeof dialog.showModal === "function") dialog.showModal();
      else dialog.setAttribute("open", "");
      closeBtn.focus();
    }

    function step(delta) {
      if (group.length <= 1) return;
      index = (index + delta + group.length) % group.length;
      render();
    }

    function close() {
      if (typeof dialog.close === "function" && dialog.open) dialog.close();
      else dialog.removeAttribute("open");
    }

    document.addEventListener("click", (e) => {
      const trigger = e.target.closest("[data-lightbox-item]");
      if (trigger) openFrom(trigger);
    });
    closeBtn.addEventListener("click", close);
    prevBtn.addEventListener("click", () => step(-1));
    nextBtn.addEventListener("click", () => step(1));
    dialog.addEventListener("click", (e) => { if (e.target === dialog) close(); });
    dialog.addEventListener("keydown", (e) => {
      if (e.key === "ArrowLeft") { e.preventDefault(); step(-1); }
      else if (e.key === "ArrowRight") { e.preventDefault(); step(1); }
    });
    dialog.addEventListener("close", () => {
      if (lastTrigger && typeof lastTrigger.focus === "function") lastTrigger.focus();
    });
  }

  // 10. Pop elements in as they scroll into view, then hand them back to their own hover styles
  function initReveal() {
    const els = Array.from(document.querySelectorAll("[data-reveal]"));
    const settle = (el) => {
      el.classList.add("is-in");
      setTimeout(() => {
        el.removeAttribute("data-reveal");
        el.classList.remove("is-in");
      }, 900);
    };
    if (reduced() || !("IntersectionObserver" in window)) {
      els.forEach((el) => el.removeAttribute("data-reveal"));
      return;
    }
    const io = new IntersectionObserver((entries) => {
      entries.filter((e) => e.isIntersecting).forEach((entry, i) => {
        setTimeout(() => settle(entry.target), i * 90);
        io.unobserve(entry.target);
      });
    }, { rootMargin: "0px 0px -8% 0px" });
    els.forEach((el) => io.observe(el));
  }

  // 11. A little confetti when someone heads off to download
  function initConfetti() {
    const shapes = ["✦", "◆", "●", "★", "♥", "♠", "♣", "♦"];
    const colors = ["#ff5e4a", "#ff7cc0", "#ffd23f", "#4fd68a", "#6b9bff", "#a487ff", "#ff9a2e"];
    document.addEventListener("click", (e) => {
      const btn = e.target.closest("[data-confetti]");
      if (!btn || reduced() || typeof document.body.animate !== "function") return;
      const r = btn.getBoundingClientRect();
      const ox = r.left + r.width / 2;
      const oy = r.top + r.height / 2;
      for (let i = 0; i < 26; i++) {
        const bit = document.createElement("span");
        bit.className = "confetti";
        bit.setAttribute("aria-hidden", "true");
        bit.textContent = shapes[i % shapes.length];
        bit.style.color = colors[i % colors.length];
        bit.style.fontSize = `${14 + Math.random() * 14}px`;
        document.body.appendChild(bit);
        const angle = Math.random() * Math.PI * 2;
        const dist = 70 + Math.random() * 140;
        const dx = Math.cos(angle) * dist;
        const dy = Math.sin(angle) * dist - 60;
        const spin = (Math.random() - 0.5) * 720;
        bit.animate(
          [
            { transform: `translate(${ox}px, ${oy}px) translate(-50%, -50%) scale(.4) rotate(0deg)`, opacity: 1 },
            { transform: `translate(${ox + dx}px, ${oy + dy}px) translate(-50%, -50%) scale(1) rotate(${spin / 2}deg)`, opacity: 1, offset: 0.55 },
            { transform: `translate(${ox + dx * 1.2}px, ${oy + dy + 160}px) translate(-50%, -50%) scale(.8) rotate(${spin}deg)`, opacity: 0 },
          ],
          { duration: 1100 + Math.random() * 500, easing: "cubic-bezier(.2,.7,.4,1)" }
        ).onfinish = () => bit.remove();
      }
    });
  }

  // 12. Play the silent in-game capture when scrolled into view (off under Reduced Motion; respects user pause)
  function initInGameVideo() {
    const videos = Array.from(document.querySelectorAll("video[data-ingame-video]"));
    if (!videos.length) return;
    for (const video of videos) {
      let userPaused = false;
      let programmaticPause = false;
      video.addEventListener("pause", () => {
        if (programmaticPause) { programmaticPause = false; return; }
        userPaused = true;
      });
      video.addEventListener("play", () => { userPaused = false; });
      if ("IntersectionObserver" in window) {
        const io = new IntersectionObserver((entries) => {
          for (const entry of entries) {
            if (entry.isIntersecting) {
              if (!reduced() && !userPaused && video.paused) {
                video.play().catch(() => {});
              }
            } else if (!video.paused) {
              programmaticPause = true;
              video.pause();
            }
          }
        }, { threshold: 0.35 });
        io.observe(video);
      }
      reducedMotionQuery.addEventListener("change", () => {
        if (reduced() && !video.paused) {
          programmaticPause = true;
          video.pause();
        }
      });
    }
  }

  applyStoreConfig();
  initThemeToggle();
  initRibbons();
  initTilt();
  initBeforeAfter();
  initDepthParallax();
  initDeferredLoops();
  initTabs();
  initLightbox();
  initReveal();
  initConfetti();
  initInGameVideo();
})();
