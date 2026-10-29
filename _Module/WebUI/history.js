(function () {
  'use strict';

  var entries = [];
  var selectedIndex = -1;
  var bound = false;
  var binding = false;
  var catalogList = document.getElementById('catalogList');
  var reader = document.getElementById('reader');
  var articleTitle = document.getElementById('articleTitle');
  var articleMeta = document.getElementById('articleMeta');
  var articleBody = document.getElementById('articleBody');
  var statusEl = document.getElementById('status');
  var scrollStates = new WeakMap();

  function setStatus(text, hide) {
    if (!statusEl) return;
    statusEl.textContent = text || '';
    if (hide) {
      statusEl.classList.add('hidden');
    } else {
      statusEl.classList.remove('hidden');
    }
  }

  function bindBridge() {
    if (bound) return Promise.resolve(true);
    if (binding) return Promise.resolve(false);
    binding = true;

    return new Promise(function (resolve) {
      if (!window.CefSharp || typeof window.CefSharp.BindObjectAsync !== 'function') {
        binding = false;
        setStatus('浏览器预览：无 CefSharp 桥', true);
        resolve(false);
        return;
      }

      CefSharp.BindObjectAsync('aiChronicle').then(function () {
        bound = true;
        binding = false;
        resolve(true);
      }).catch(function (err) {
        binding = false;
        setStatus('CEF 桥接失败：' + (err && err.message ? err.message : err));
        resolve(false);
      });
    });
  }

  function clear(node) {
    while (node && node.firstChild) node.removeChild(node.firstChild);
  }

  function renderCatalog() {
    if (!catalogList) return;
    clear(catalogList);

    entries.forEach(function (entry) {
      if (entry.header) {
        var header = document.createElement('div');
        header.className = 'group';
        header.textContent = entry.title || '';
        catalogList.appendChild(header);
        return;
      }

      var item = document.createElement('div');
      item.className = 'item' + (entry.index === selectedIndex ? ' selected' : '');
      item.textContent = entry.title || '';
      item.setAttribute('data-index', String(entry.index));
      item.addEventListener('click', function () {
        selectEntry(entry.index);
      });
      catalogList.appendChild(item);

      if (entry.index === selectedIndex) {
        setTimeout(function () {
          if (item.scrollIntoView) item.scrollIntoView({ block: 'nearest' });
        }, 0);
      }
    });
  }

  function selectEntry(index) {
    if (entries.length === 0) return;
    selectedIndex = index;
    renderCatalog();
    setStatus('研墨中…');

    bindBridge().then(function (ok) {
      if (!ok) {
        setStatus('CefSharp 桥不可用，无法打开史册条目');
        return;
      }
      try {
        aiChronicle.dispatch('select', String(index));
      } catch (err) {
        setStatus('打开史册失败：' + (err && err.message ? err.message : err));
      }
    });
  }

  function appendHeading(text) {
    var h = document.createElement('h2');
    h.textContent = text;
    articleBody.appendChild(h);
  }

  function appendParagraph(text) {
    if (!text) return;
    var p = document.createElement('p');
    p.textContent = text;
    articleBody.appendChild(p);
  }

  function renderArticleBody(data) {
    if (!articleBody) return;
    clear(articleBody);

    var body = String(data.body || '').replace(/\r/g, '');
    var lines = body.split(/\n/);
    var buffer = [];
    var skippedTitle = false;

    function flush() {
      if (buffer.length > 0) {
        appendParagraph(buffer.join(' '));
        buffer = [];
      }
    }

    lines.forEach(function (line) {
      var text = line.replace(/^\s+|\s+$/g, '');
      if (!text) {
        flush();
        return;
      }

      if (/^【[^】]+】$/.test(text)) {
        flush();
        appendHeading(text);
        return;
      }

      if (!skippedTitle) {
        if (text === data.title) {
          skippedTitle = true;
          return;
        }
        skippedTitle = true;
      }

      buffer.push(text);
    });

    flush();

    if (articleBody.childNodes.length === 0) {
      appendParagraph('（此篇暂无正文）');
    }
  }

  function renderArticle(data) {
    if (!data) return;

    if (typeof data.index === 'number') {
      selectedIndex = data.index;
      renderCatalog();
    }

    var fontSize = Number(data.fontSize) || 18;
    fontSize = Math.max(14, Math.min(34, fontSize));
    document.documentElement.style.setProperty('--reader-font-size', fontSize + 'px');

    articleTitle.textContent = data.title || '无题';
    articleMeta.textContent = '史册 · 第 ' + (selectedIndex + 1) + ' 篇';
    renderArticleBody(data);
    setStatus('', true);

    if (reader) reader.scrollTop = 0;
  }

  function smoothScroll(el, delta) {
    if (!el) return;
    var state = scrollStates.get(el);
    if (!state) {
      state = { target: el.scrollTop, raf: 0 };
      scrollStates.set(el, state);
    }

    var max = Math.max(0, el.scrollHeight - el.clientHeight);
    state.target = Math.max(0, Math.min(max, state.target + delta));

    if (state.raf) return;

    function step() {
      var diff = state.target - el.scrollTop;
      if (Math.abs(diff) < 0.5) {
        el.scrollTop = state.target;
        state.raf = 0;
        return;
      }
      el.scrollTop = el.scrollTop + diff * 0.32;
      state.raf = requestAnimationFrame(step);
    }

    state.raf = requestAnimationFrame(step);
  }

  window.__scrollBySmooth = function (delta, x, y) {
    var node = document.elementFromPoint(x, y);
    var scroller = node && node.closest ? node.closest('.scrollable') : null;
    if (!scroller) scroller = reader;
    smoothScroll(scroller, delta);
  };

  window.__game = {
    render: function (data) {
      data = data || {};
      entries = data.entries || [];
      selectedIndex = data.article && typeof data.article.index === 'number' ? data.article.index : -1;
      renderCatalog();
      if (data.article) {
        renderArticle(data.article);
      } else {
        setStatus('尚未选择史册条目');
      }
    },
    showArticle: function (data) {
      renderArticle(data);
    },
    showError: function (message) {
      setStatus('史册读取失败：' + (message || '未知错误'));
    }
  };

  bindBridge();
  setStatus('研墨中…');
})();