(function () {
  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }

  function render(data) {
    data = data || {};
    document.body.className = "page " + (data.template || "chronicle");
    document.title = data.title || "AI Chronicle";

    const app = document.getElementById("app");
    if (!app) return;
    app.innerHTML = "";

    const sheet = el("article", "sheet");
    if (data.eyebrow) sheet.appendChild(el("div", "eyebrow", data.eyebrow));
    if (data.title) sheet.appendChild(el("h1", "title", data.title));
    if (data.subtitle) sheet.appendChild(el("div", "subtitle", data.subtitle));
    sheet.appendChild(el("div", "rule"));

    (data.sections || []).forEach(function (section) {
      if (!section) return;
      const node = el("section", "section");
      if (section.heading) node.appendChild(el("h2", "", section.heading));
      (section.paragraphs || []).forEach(function (paragraph) {
        if (paragraph) node.appendChild(el("p", "", paragraph));
      });
      sheet.appendChild(node);
    });

    if (data.note) {
      const note = el("aside", "note");
      note.appendChild(el("div", "label", data.noteLabel || "史官曰"));
      note.appendChild(el("p", "", data.note));
      sheet.appendChild(note);
    }

    if (data.seal) {
      sheet.appendChild(el("div", "seal", data.seal));
    }

    if (data.footer) {
      const footer = el("footer", "footer");
      footer.appendChild(el("span", "", data.footer));
      if (data.page) footer.appendChild(el("span", "", data.page));
      sheet.appendChild(footer);
    }

    app.appendChild(sheet);
  }

  // 平滑滚动：滚轮事件只累积目标位置，页面逐帧逼近，避免一次滚动直接跳到底。
  var scrollTarget = null;
  var scrollRaf = 0;

  window.__scrollBySmooth = function (delta) {
    var max = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
    var base = scrollTarget === null ? window.scrollY : scrollTarget;
    scrollTarget = Math.max(0, Math.min(max, base + delta));

    if (scrollRaf) return;

    function step() {
      var diff = scrollTarget - window.scrollY;
      if (Math.abs(diff) < 0.5) {
        window.scrollTo(0, scrollTarget);
        scrollTarget = null;
        scrollRaf = 0;
        return;
      }
      window.scrollTo(0, window.scrollY + diff * 0.30);
      scrollRaf = requestAnimationFrame(step);
    }

    scrollRaf = requestAnimationFrame(step);
  };

  window.AIChroniclePage = { render: render };
  window.__game = window.__game || {};
  window.__game.render = function (data) {
    scrollTarget = null;
    render(data);
  };

  if (window.__PAGE__) render(window.__PAGE__);
})();