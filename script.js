/* Full Art Spire — Showcase interactions (vanilla JS, zero dependencies) */

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
      const isValid = /^https:\/\/.+/i.test(trimmed);
      const badge = el.querySelector(".btn-badge");
      if (isValid) {
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

  // 2. Accessible Before / After sliders driven by native <input type="range">
  function initBeforeAfter() {
    const viewports = document.querySelectorAll("[data-ba]");
    for (const viewport of viewports) {
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

  // 3. Interactive browser parallax approximation from shipped background + foreground mask
  function initDepthParallax() {
    const stages = document.querySelectorAll("[data-depth-stage]");
    const hints = document.querySelectorAll("[data-depth-hint]");

    function syncHintText() {
      const reduced = reducedMotionQuery.matches;
      for (const hint of hints) {
        hint.textContent = reduced
          ? "Static view (Reduced Motion is enabled)"
          : "Move pointer or drag across the card";
      }
      if (reduced) {
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
        if (reducedMotionQuery.matches) return;
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
        if (e.pointerType === "mouse" && !reducedMotionQuery.matches) {
          stage.classList.add("is-interacting");
          setOffset(e.clientX, e.clientY);
        }
      });

      stage.addEventListener("pointermove", (e) => {
        if (reducedMotionQuery.matches) return;
        if (e.pointerType === "mouse" || draggingTouch) {
          stage.classList.add("is-interacting");
          setOffset(e.clientX, e.clientY);
        }
      });

      stage.addEventListener("pointerdown", (e) => {
        if (reducedMotionQuery.matches) return;
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

  // 3b. Upgrade below-the-fold animated rarity finish loops only when scrolled into view
  function initDeferredLoops() {
    const imgs = Array.from(document.querySelectorAll("img[data-anim-src]"));
    if (!imgs.length) return;

    function applyMotionPreference() {
      if (reducedMotionQuery.matches) {
        for (const img of imgs) {
          if (img.dataset.stillSrc) img.src = img.dataset.stillSrc;
        }
      } else {
        for (const img of imgs) {
          if (img.dataset.activated === "true" && img.dataset.animSrc) {
            img.src = img.dataset.animSrc;
          }
        }
      }
    }
    reducedMotionQuery.addEventListener("change", applyMotionPreference);

    const activateImg = (img) => {
      img.dataset.activated = "true";
      if (!reducedMotionQuery.matches && img.dataset.animSrc) {
        img.src = img.dataset.animSrc;
      }
    };

    if ("IntersectionObserver" in window) {
      const io = new IntersectionObserver(
        (entries) => {
          for (const entry of entries) {
            if (entry.isIntersecting) {
              activateImg(entry.target);
              io.unobserve(entry.target);
            }
          }
        },
        { rootMargin: "160px 0px" }
      );
      for (const img of imgs) io.observe(img);
    } else {
      for (const img of imgs) activateImg(img);
    }
  }

  // 4. Accessible ARIA tabs for the character gallery
  function initTabs() {
    const tablist = document.querySelector("[data-gallery-tabs]");
    if (!tablist) return;
    const tabs = Array.from(tablist.querySelectorAll('[role="tab"]'));

    function hydratePanel(panel) {
      if (!panel) return;
      for (const img of panel.querySelectorAll("img[data-src]")) {
        img.src = img.dataset.src;
        img.removeAttribute("data-src");
      }
    }

    function activate(tab, moveFocus) {
      for (const candidate of tabs) {
        const selected = candidate === tab;
        candidate.setAttribute("aria-selected", selected ? "true" : "false");
        candidate.tabIndex = selected ? 0 : -1;
        const panelId = candidate.getAttribute("aria-controls");
        const panel = panelId ? document.getElementById(panelId) : null;
        if (panel) {
          panel.hidden = !selected;
          if (selected) hydratePanel(panel);
        }
      }
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

  // 5. Accessible card lightbox (<dialog>) with prev/next navigation
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

    let currentGroup = [];
    let currentIndex = 0;
    let lastTrigger = null;

    function renderCurrent() {
      const btn = currentGroup[currentIndex];
      if (!btn) return;
      const fullSrc = btn.dataset.full || "";
      const name = btn.dataset.name || "";
      const meta = btn.dataset.meta || "";
      titleEl.textContent = name;
      subEl.textContent = meta;
      imgEl.src = fullSrc;
      imgEl.alt = `${name} (${meta}) — full-art card`;
      counterEl.textContent = `${currentIndex + 1} / ${currentGroup.length}`;
      prevBtn.disabled = currentGroup.length <= 1;
      nextBtn.disabled = currentGroup.length <= 1;
    }

    function openFrom(trigger) {
      const scope = trigger.closest("[data-lightbox-group]") || document;
      currentGroup = Array.from(scope.querySelectorAll("[data-lightbox-item]"));
      currentIndex = Math.max(0, currentGroup.indexOf(trigger));
      lastTrigger = trigger;
      renderCurrent();
      if (typeof dialog.showModal === "function") {
        dialog.showModal();
      } else {
        dialog.setAttribute("open", "");
      }
      closeBtn.focus();
    }

    function step(delta) {
      if (currentGroup.length <= 1) return;
      currentIndex = (currentIndex + delta + currentGroup.length) % currentGroup.length;
      renderCurrent();
    }

    function closeDialog() {
      if (typeof dialog.close === "function" && dialog.open) {
        dialog.close();
      } else {
        dialog.removeAttribute("open");
      }
    }

    document.addEventListener("click", (e) => {
      const trigger = e.target.closest("[data-lightbox-item]");
      if (trigger) openFrom(trigger);
    });

    closeBtn.addEventListener("click", closeDialog);
    prevBtn.addEventListener("click", () => step(-1));
    nextBtn.addEventListener("click", () => step(1));

    dialog.addEventListener("click", (e) => {
      if (e.target === dialog) closeDialog();
    });

    dialog.addEventListener("keydown", (e) => {
      if (e.key === "ArrowLeft") {
        e.preventDefault();
        step(-1);
      } else if (e.key === "ArrowRight") {
        e.preventDefault();
        step(1);
      }
    });

    dialog.addEventListener("close", () => {
      if (lastTrigger && typeof lastTrigger.focus === "function") {
        lastTrigger.focus();
      }
    });
  }

  applyStoreConfig();
  initBeforeAfter();
  initDepthParallax();
  initDeferredLoops();
  initTabs();
  initLightbox();
})();
