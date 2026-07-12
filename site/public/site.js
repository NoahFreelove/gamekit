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
