(function () {
  'use strict';

  var entries = [];
  var selectedIndex = -1;
  var bound = false;
  var binding = false;
  var catalogList = document.getElementById('catalogList');
  var catalogCount = document.getElementById('catalogCount');
  var reader = document.getElementById('reader');
  var articleTitle = document.getElementById('articleTitle');
  var articleMeta = document.getElementById('articleMeta');
  var articleCategory = document.getElementById('articleCategory');
  var articleYear = document.getElementById('articleYear');
  var mastDot = document.getElementById('mastDot');
  var articleNumber = document.getElementById('articleNumber');
  var articleTotal = document.getElementById('articleTotal');
  var articleBody = document.getElementById('articleBody');
  var articleEnd = document.getElementById('articleEnd');
  var progressBar = document.getElementById('progressBar');
  var pageLabel = document.getElementById('pageLabel');
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

  function setText(node, text) {
    if (node) node.textContent = text == null ? '' : String(text);
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

  function selectableEntries() {
    return entries.filter(function (entry) { return !entry.header; });
  }

  function pad2(value) {
    return value < 10 ? '0' + value : String(value);
  }

  function categoryFor(index) {
    var category = '史册';
    for (var i = 0; i < entries.length; i++) {
      if (entries[i].index === index) return category;
      if (entries[i].header) category = entries[i].title || category;
    }
    return category;
  }

  function countCharacters(body) {
    return String(body || '').replace(/\s+/g, '').length;
  }

  function formatNumber(value) {
    return String(value).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  }

  function renderCatalog() {
    if (!catalogList) return;
    clear(catalogList);

    var selectable = selectableEntries();
    setText(catalogCount, selectable.length > 0 ? selectable.length + ' 篇' : '');

    if (entries.length === 0) {
      var empty = document.createElement('div');
      empty.className = 'catalog-empty';
      empty.textContent = '史官尚未落笔。' + String.fromCharCode(10) + '待天下有事，自有记载。';
      catalogList.appendChild(empty);
      return;
    }

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

  function appendParagraph(text, lead) {
    if (!text) return;
    var p = document.createElement('p');
    if (lead) p.className = 'lead';
    p.textContent = text;
    articleBody.appendChild(p);
  }

  /* ---------- Markdown（史官成文） ---------- */

  // 只用 DOM 节点构造正文，不拼 innerHTML；史文里的 < > 永远是文本。
  function appendInline(parent, text) {
    var value = String(text == null ? '' : text);
    // 史官被要求不用 HTML；若模型仍写出标签，按原生 fallback 的规则去掉，只留文字。
    value = value.replace(/<[^>]+>/g, '');
    // 链接/图片只保留文字：史书不需要可点击链接，也避免页面跳转。
    value = value.replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
                 .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1');

    var pattern = /(\*\*[^*]+\*\*|__[^_]+__|\*[^*]+\*|_[^_]+_|`[^`]+`)/g;
    var lastIndex = 0;
    var match;
    while ((match = pattern.exec(value)) !== null) {
      if (match.index > lastIndex) {
        parent.appendChild(document.createTextNode(value.slice(lastIndex, match.index)));
      }

      var token = match[0];
      var node;
      if (token.slice(0, 2) === '**' || token.slice(0, 2) === '__') {
        node = document.createElement('strong');
        node.textContent = token.slice(2, -2);
      } else if (token.charAt(0) === '`') {
        node = document.createElement('code');
        node.textContent = token.slice(1, -1);
      } else {
        node = document.createElement('em');
        node.textContent = token.slice(1, -1);
      }
      parent.appendChild(node);
      lastIndex = match.index + token.length;
    }

    if (lastIndex < value.length) {
      parent.appendChild(document.createTextNode(value.slice(lastIndex)));
    }
  }

  function joinLines(lines) {
    var result = '';
    lines.forEach(function (line) {
      var value = String(line || '').trim();
      if (!value) return;
      if (!result) {
        result = value;
        return;
      }
      var prev = result.charAt(result.length - 1);
      var next = value.charAt(0);
      var cjk = /[\u3000-\u303f\u3400-\u9fff\uff00-\uffef]/;
      result += (cjk.test(prev) && cjk.test(next)) ? '' : ' ';
      result += value;
    });
    return result;
  }

  function isDuplicateTitle(text, title) {
    var value = String(text == null ? '' : text).replace(/^#+\s*/, '').trim();
    var expected = String(title == null ? '' : title).trim();
    if (!expected) return false;
    if (value === expected) return true;
    return value.length > expected.length &&
           value.length - expected.length <= 12 &&
           value.slice(-expected.length) === expected;
  }

  function isBlockStart(line) {
    var text = String(line || '').trim();
    return /^(#{1,6})\s+/.test(text) ||
           /^```/.test(text) ||
           /^(-{3,}|\*{3,}|_{3,})$/.test(text) ||
           /^>/.test(text) ||
           /^【[^】]+】$/.test(text) ||
           /^\s*[-*+]\s+/.test(line) ||
           /^\s*\d+[.)]\s+/.test(line);
  }

  function renderMarkdownBody(body, title) {
    var lines = String(body || '').replace(/\r/g, '').split('\n');
    var i = 0;
    var firstBlock = true;
    var leadAssigned = false;

    function appendBodyParagraph(text) {
      if (!text) return;
      var p = document.createElement('p');
      if (!leadAssigned) {
        p.className = 'lead';
        leadAssigned = true;
      }
      appendInline(p, text);
      articleBody.appendChild(p);
    }

    while (i < lines.length) {
      var line = lines[i];
      var text = line.trim();
      if (!text) {
        i++;
        continue;
      }

      if (firstBlock) {
        var firstHeading = text.match(/^(#{1,6})\s+(.*)$/);
        var firstText = firstHeading ? firstHeading[2].trim() : text;
        if (isDuplicateTitle(firstText, title)) {
          firstBlock = false;
          i++;
          continue;
        }
        firstBlock = false;
      }

      if (/^```/.test(text)) {
        var codeLines = [];
        i++;
        while (i < lines.length && !/^```/.test(lines[i].trim())) {
          codeLines.push(lines[i]);
          i++;
        }
        if (i < lines.length) i++;
        var pre = document.createElement('pre');
        var code = document.createElement('code');
        code.textContent = codeLines.join('\n');
        pre.appendChild(code);
        articleBody.appendChild(pre);
        continue;
      }

      var heading = text.match(/^(#{1,6})\s+(.*)$/);
      if (heading) {
        var level = Math.min(3, Math.max(2, heading[1].length));
        var node = document.createElement('h' + level);
        var label = document.createElement('span');
        appendInline(label, heading[2]);
        node.appendChild(label);
        articleBody.appendChild(node);
        i++;
        continue;
      }

      if (/^【[^】]+】$/.test(text)) {
        appendHeading(text);
        i++;
        continue;
      }

      if (/^(-{3,}|\*{3,}|_{3,})$/.test(text)) {
        articleBody.appendChild(document.createElement('hr'));
        i++;
        continue;
      }

      if (/^>/.test(line)) {
        var quoteLines = [];
        while (i < lines.length && /^\s*>/.test(lines[i])) {
          quoteLines.push(lines[i].replace(/^\s*>\s?/, ''));
          i++;
        }
        var quote = document.createElement('blockquote');
        var quoteParagraph = document.createElement('p');
        appendInline(quoteParagraph, joinLines(quoteLines));
        quote.appendChild(quoteParagraph);
        articleBody.appendChild(quote);
        continue;
      }

      if (/^\s*[-*+]\s+/.test(line)) {
        var ul = document.createElement('ul');
        while (i < lines.length && /^\s*[-*+]\s+/.test(lines[i])) {
          var li = document.createElement('li');
          appendInline(li, lines[i].replace(/^\s*[-*+]\s+/, ''));
          ul.appendChild(li);
          i++;
        }
        articleBody.appendChild(ul);
        continue;
      }

      if (/^\s*\d+[.)]\s+/.test(line)) {
        var ol = document.createElement('ol');
        while (i < lines.length && /^\s*\d+[.)]\s+/.test(lines[i])) {
          var oli = document.createElement('li');
          appendInline(oli, lines[i].replace(/^\s*\d+[.)]\s+/, ''));
          ol.appendChild(oli);
          i++;
        }
        articleBody.appendChild(ol);
        continue;
      }

      var buffer = [line];
      i++;
      while (i < lines.length) {
        if (!lines[i].trim() || isBlockStart(lines[i])) break;
        buffer.push(lines[i]);
        i++;
      }

      appendBodyParagraph(joinLines(buffer));

      if (i < lines.length && !lines[i].trim()) i++;
    }
  }

  /* ---------- 纯文本（谏言/诏令/密陈，保持原样） ---------- */

  function renderPlainBody(body, title) {
    var lines = String(body || '').replace(/\r/g, '').split('\n');
    var buffer = [];
    var skippedTitle = false;
    var leadAssigned = false;

    function flush() {
      if (buffer.length > 0) {
        var p = document.createElement('p');
        if (!leadAssigned) {
          p.className = 'lead';
          leadAssigned = true;
        }
        p.textContent = buffer.join(' ');
        articleBody.appendChild(p);
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
        if (text === title) {
          skippedTitle = true;
          return;
        }
        skippedTitle = true;
      }

      buffer.push(text);
    });

    flush();
  }

  function renderArticleBody(data) {
    if (!articleBody) return;
    clear(articleBody);
    if (articleEnd) articleEnd.classList.add('hidden');

    if (data.error) {
      var error = document.createElement('p');
      error.className = 'error';
      error.textContent = '（读取失败：' + String(data.error) + '）';
      articleBody.appendChild(error);
      return;
    }

    var body = String(data.body || '').replace(/\r/g, '');
    var format = String(data.format || 'plain').toLowerCase();
    if (format === 'markdown') {
      renderMarkdownBody(body, data.title);
    } else {
      renderPlainBody(body, data.title);
    }

    if (articleBody.childNodes.length === 0) {
      appendParagraph('（此篇暂无正文）');
      return;
    }

    if (articleEnd) articleEnd.classList.remove('hidden');
  }

  function updateProgress() {
    if (!reader || !progressBar) return;
    var max = Math.max(0, reader.scrollHeight - reader.clientHeight);
    var ratio = max > 0 ? Math.max(0, Math.min(1, reader.scrollTop / max)) : 0;
    progressBar.style.width = (ratio * 100).toFixed(2) + '%';
  }

  function renderArticle(data) {
    if (!data) return;

    if (typeof data.index === 'number') {
      selectedIndex = data.index;
      renderCatalog();
    }

    var nativeSize = Number(data.fontSize) || 28;
    nativeSize = Math.max(14, Math.min(36, nativeSize));
    // 原生 MCM 字号是 Gauntlet 单位；Web 页按阅读区宽度重映射，28 → 22px。
    var webSize = Math.round(16 + (nativeSize - 14) * 0.4);
    document.documentElement.style.setProperty('--reader-font-size', webSize + 'px');

    var selectable = selectableEntries();
    var category = data.category || categoryFor(selectedIndex) || '史册';
    var position = Number(data.position) || 0;
    var total = Number(data.total) || selectable.length;
    var year = Number(data.year) || 0;
    var body = String(data.body || '');
    var characters = countCharacters(body);

    if (articleTitle) articleTitle.textContent = data.title || '无题';
    setText(articleCategory, category);
    setText(articleYear, year > 0 ? '第 ' + year + ' 年' : '');
    if (mastDot) mastDot.style.display = year > 0 ? '' : 'none';
    setText(articleNumber, pad2(position || (selectedIndex + 1)));
    setText(articleTotal, pad2(total || selectable.length));
    setText(articleMeta, (characters > 0 ? '约 ' + formatNumber(characters) + ' 字 · ' : '') +
      (String(data.format || '').toLowerCase() === 'markdown' ? '史官手稿' : '档案原件'));

    var pages = total > 0 && position > 0
      ? '第 ' + position + ' 篇 · 共 ' + total + ' 篇'
      : '';
    setText(pageLabel, pages);

    renderArticleBody(data);
    setStatus('', true);

    if (reader) reader.scrollTop = 0;
    updateProgress();
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
        setStatus(entries.length ? '选择左侧一篇史册开始阅读' : '史官尚未落笔');
      }
    },
    showArticle: function (data) {
      renderArticle(data);
    },
    showError: function (message) {
      setStatus('史册读取失败：' + (message || '未知错误'));
    }
  };

  if (reader) reader.addEventListener('scroll', updateProgress, { passive: true });

  bindBridge();
  setStatus('研墨中…');
})();
