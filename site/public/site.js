// Copy-to-clipboard for the install command. That's the entire script —
// no analytics, no external requests, matching GameKit's no-phone-home ethos.
(function () {
  "use strict";
  document.querySelectorAll(".copy-btn").forEach(function (btn) {
    var label = btn.textContent;
    btn.addEventListener("click", function () {
      var text = btn.getAttribute("data-copy") || "";
      function done() {
        btn.classList.add("copied");
        btn.textContent = "copied";
        setTimeout(function () {
          btn.classList.remove("copied");
          btn.textContent = label;
        }, 1600);
      }
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(done).catch(function () {});
      } else {
        // Fallback for non-secure contexts (e.g. plain-http LAN preview).
        var ta = document.createElement("textarea");
        ta.value = text;
        ta.setAttribute("readonly", "");
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        try { document.execCommand("copy"); done(); } catch (e) {}
        document.body.removeChild(ta);
      }
    });
  });
})();

// Click-to-enlarge lightbox for the admin-console screenshots. Progressive
// enhancement: without this script the dialog stays hidden and the grid is
// plain figures. Class/attribute toggling only — no inline styles, no
// external requests, no globals.
(function () {
  "use strict";

  var lightbox = document.getElementById("shot-lightbox");
  var figures = document.querySelectorAll("#console .shot");
  if (!lightbox || figures.length === 0) { return; }

  var lbImg = document.getElementById("lb-img");
  var lbTitle = document.getElementById("lb-title");
  var lbCaption = document.getElementById("lb-caption");
  var lbCounter = document.getElementById("lb-counter");
  var closeBtn = lightbox.querySelector(".lb-close");
  var prevBtn = lightbox.querySelector(".lb-prev");
  var nextBtn = lightbox.querySelector(".lb-next");
  var shots = document.querySelector("#console .shots");

  var slides = [];
  var current = 0;
  var returnFocus = null;

  Array.prototype.forEach.call(figures, function (fig) {
    var img = fig.querySelector("img");
    var caption = fig.querySelector("figcaption");
    var title = fig.querySelector(".term-title");
    slides.push({
      src: img.getAttribute("src"),
      alt: img.alt,
      caption: caption ? caption.textContent.trim() : "",
      title: title ? title.textContent.trim() : ""
    });
  });

  function render(i) {
    current = i;
    lbImg.src = slides[i].src; // same-origin relative path — CSP img-src 'self' fine
    lbImg.alt = slides[i].alt;
    lbTitle.textContent = slides[i].title;
    lbCaption.textContent = slides[i].caption;
    lbCounter.textContent = (i + 1) + " / " + slides.length;
  }

  function onKeydown(e) {
    if (e.key === "Escape") {
      close();
    } else if (e.key === "ArrowRight") {
      render((current + 1) % slides.length);
    } else if (e.key === "ArrowLeft") {
      render((current - 1 + slides.length) % slides.length);
    } else if (e.key === "Tab") {
      // Minimal focus containment across the dialog's three buttons.
      if (e.shiftKey && document.activeElement === closeBtn) {
        e.preventDefault();
        nextBtn.focus();
      } else if (!e.shiftKey && document.activeElement === nextBtn) {
        e.preventDefault();
        closeBtn.focus();
      }
    }
  }

  function open(i, trigger) {
    returnFocus = trigger;
    render(i);
    lightbox.removeAttribute("hidden");
    lightbox.classList.add("is-open");
    document.body.classList.add("lb-locked");
    document.addEventListener("keydown", onKeydown);
    closeBtn.focus();
  }

  function close() {
    lightbox.setAttribute("hidden", "");
    lightbox.classList.remove("is-open");
    document.body.classList.remove("lb-locked");
    document.removeEventListener("keydown", onKeydown);
    if (returnFocus) { returnFocus.focus(); }
  }

  Array.prototype.forEach.call(figures, function (fig, i) {
    var trigger = fig.querySelector(".term-shot");
    if (!trigger) { return; }
    trigger.setAttribute("tabindex", "0");
    trigger.setAttribute("role", "button");
    trigger.setAttribute("aria-haspopup", "dialog");
    trigger.setAttribute("aria-label", "Enlarge screenshot: " + slides[i].caption);
    trigger.addEventListener("click", function () { open(i, trigger); });
    trigger.addEventListener("keydown", function (e) {
      if (e.key === "Enter") {
        // Without preventDefault the browser's default Enter activation
        // fires a click on the close button that open() just focused,
        // closing the dialog in the same keystroke.
        e.preventDefault();
        open(i, trigger);
      } else if (e.key === " " || e.key === "Spacebar") {
        e.preventDefault(); // Space must not scroll the page
        open(i, trigger);
      }
    });
  });

  if (shots) { shots.classList.add("lb-enhanced"); }

  closeBtn.addEventListener("click", close);
  prevBtn.addEventListener("click", function () {
    render((current - 1 + slides.length) % slides.length);
  });
  nextBtn.addEventListener("click", function () {
    render((current + 1) % slides.length);
  });
  lightbox.addEventListener("click", function (e) {
    if (e.target === e.currentTarget) { close(); } // backdrop click
  });
})();
