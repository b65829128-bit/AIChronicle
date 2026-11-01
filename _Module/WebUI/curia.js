(function () {
  'use strict';

  var state = {
    data: null,
    tab: 'curia',
    selectedId: null,
    bridge: false,
    binding: false
  };

  var viewEl = document.getElementById('view');
  var navEl = document.getElementById('nav');
  var statusEl = document.getElementById('status');
  var topEyebrow = document.getElementById('topEyebrow');
  var topTitle = document.getElementById('topTitle');
  var topKingdom = document.getElementById('topKingdom');
  var topDate = document.getElementById('topDate');
  var footHint = document.getElementById('footHint');

  var TABS = [
    { id: 'curia', title: '朝议', sub: '提案 · 表决 · 记录' },
    { id: 'diplomacy', title: '外交', sub: '外国提案 · 战和 · 问询' },
    { id: 'fiefs', title: '封地', sub: '转让 · 夺封 · 驱逐' },
    { id: 'secret', title: '密陈', sub: '只呈国王' },
    { id: 'secretary', title: '秘书', sub: '仅查询' }
  ];

  var TYPE_LABEL = {
    declare_war: '宣战',
    propose_peace: '议和提议',
    propose_trade: '贸易提议',
    propose_alliance: '结盟提议',
    break_alliance: '退出盟约',
    enact_policy: '提出政策',
    revoke_policy: '废除政策',
    transfer_fief: '封地转让',
    expel_clan: '驱逐家族'
  };

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = String(text);
    return n;
  }

  function clear(n) {
    while (n && n.firstChild) n.removeChild(n.firstChild);
  }

  // CEF OffScreen 不弹原生 <select> 下拉框；用页面内按钮+面板替代，保证点击可用。
  var _openDropdown = null;

  function createDropdown(options, initialValue, onChange) {
    options = options || [];
    var wrap = el('div', 'dropdown');
    var btn = el('button', 'dropdown-toggle');
    btn.type = 'button';
    var menu = el('div', 'dropdown-menu hidden');
    var ctrl = { value: initialValue || '', el: wrap, onChange: onChange, card: null };

    function labelFor(value) {
      for (var i = 0; i < options.length; i++) {
        if (String(options[i].value) === String(value)) return options[i].label;
      }
      return options.length ? '（请选择）' : '（无选项）';
    }

    function setValue(value, fire) {
      ctrl.value = value == null ? '' : String(value);
      btn.textContent = labelFor(ctrl.value);
      if (fire && ctrl.onChange) ctrl.onChange(ctrl.value);
    }

    function close() {
      menu.classList.add('hidden');
      if (ctrl.card) ctrl.card.classList.remove('dropdown-open');
      if (_openDropdown === ctrl) _openDropdown = null;
    }

    ctrl.close = close;

    function renderOptions() {
      clear(menu);
      options.forEach(function (opt) {
        var item = el('button', 'dropdown-item', opt.label);
        item.type = 'button';
        item.setAttribute('data-value', String(opt.value));
        item.addEventListener('click', function (e) {
          e.stopPropagation();
          setValue(opt.value, true);
          close();
        });
        menu.appendChild(item);
      });
    }

    function setOptions(next, preferred) {
      close();
      options = next || [];
      renderOptions();
      var keep = preferred == null ? '' : String(preferred);
      var exists = options.some(function (o) { return String(o.value) === keep; });
      setValue(exists ? keep : (options.length ? options[0].value : ''), false);
    }

    ctrl.setOptions = setOptions;
    renderOptions();

    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      if (!menu.classList.contains('hidden')) {
        close();
        return;
      }
      if (_openDropdown && _openDropdown !== ctrl) _openDropdown.close();
      menu.classList.remove('hidden');
      ctrl.card = wrap.closest ? wrap.closest('.card') : null;
      if (ctrl.card) ctrl.card.classList.add('dropdown-open');
      _openDropdown = ctrl;
    });

    wrap.appendChild(btn);
    wrap.appendChild(menu);
    if (!ctrl.value && options.length) ctrl.value = String(options[0].value);
    setValue(ctrl.value, false);
    return ctrl;
  }

  document.addEventListener('click', function () {
    if (_openDropdown) {
      _openDropdown.close();
      _openDropdown = null;
    }
  });

  window.__scrollBySmooth = function (delta, x, y) {
    var node = document.elementFromPoint(x, y);
    var scroller = null;
    var cur = node;
    while (cur && cur !== document.body) {
      if (cur.scrollHeight > cur.clientHeight + 1) {
        var style = window.getComputedStyle(cur);
        if (style.overflowY === 'auto' || style.overflowY === 'scroll') {
          scroller = cur;
          break;
        }
      }
      cur = cur.parentElement;
    }
    if (!scroller) scroller = document.querySelector('.main.scrollable');
    if (!scroller) return;
    scroller.scrollTop += Number(delta) || 0;
  };

  function setStatus(text, bad) {
    if (!statusEl) return;
    statusEl.textContent = text || '';
    statusEl.classList.remove('hidden');
    statusEl.classList.toggle('bad', !!bad);
    if (text) {
      window.setTimeout(function () {
        statusEl.classList.add('hidden');
      }, 2600);
    }
  }

  function bindBridge() {
    if (state.bridge) return Promise.resolve(true);
    if (state.binding) return Promise.resolve(false);
    state.binding = true;
    return new Promise(function (resolve) {
      if (!window.CefSharp || typeof window.CefSharp.BindObjectAsync !== 'function') {
        state.binding = false;
        resolve(false);
        return;
      }
      CefSharp.BindObjectAsync('aiChronicle').then(function () {
        state.bridge = true;
        state.binding = false;
        resolve(true);
      }).catch(function () {
        state.binding = false;
        resolve(false);
      });
    });
  }

  function dispatch(id, payload) {
    bindBridge().then(function (ok) {
      if (!ok) {
        setStatus('CEF 桥不可用', true);
        return;
      }
      try {
        aiChronicle.dispatch(id, payload == null ? '' : JSON.stringify(payload));
      } catch (e) {
        setStatus('操作失败：' + (e && e.message ? e.message : e), true);
      }
    });
  }

  function fmt(n) {
    var v = Number(n || 0);
    return v.toFixed(1);
  }

  function pct(n) {
    var v = Number(n || 0);
    return v.toFixed(1) + '%';
  }

  function statusClass(s) {
    if (s === 'Applied' || s === 'Passed') return 'pass';
    if (s === 'Rejected' || s === 'Failed' || s === 'Cancelled') return 'fail';
    return 'wait';
  }

  function renderNav() {
    clear(navEl);
    TABS.forEach(function (t) {
      var b = el('button', state.tab === t.id ? 'active' : '');
      b.appendChild(el('span', '', t.title));
      b.appendChild(el('span', 'nav-sub', t.sub));
      b.addEventListener('click', function () {
        state.tab = t.id;
        state.selectedId = null;
        renderAll();
      });
      navEl.appendChild(b);
    });
  }

  function renderTop() {
    var d = state.data || {};
    var tab = TABS.filter(function (t) { return t.id === state.tab; })[0] || TABS[0];
    topEyebrow.textContent = d.kingdom ? (d.kingdom + ' · 参政') : '卡拉迪亚 · 参政';
    topTitle.textContent = tab.title;
    topKingdom.textContent = d.kingdom || '无王国';
    topDate.textContent = d.currentDate || '';
    footHint.textContent = d.player && d.player.isMercenary ? '雇佣兵不参与朝议' : '投票公开 · 史官可读';
  }

  function renderAll() {
    renderNav();
    renderTop();
    clear(viewEl);
    if (!state.data) {
      viewEl.appendChild(el('div', 'empty', '正在研墨…'));
      return;
    }
    var d = state.data;
    if (d.notice) {
      var notice = el('div', 'notice', d.notice);
      if (d.player && d.player.isMercenary) notice.classList.add('bad');
      viewEl.appendChild(notice);
    }
    if (state.tab === 'curia') renderCuria();
    else if (state.tab === 'diplomacy') renderDiplomacy();
    else if (state.tab === 'fiefs') renderFiefs();
    else if (state.tab === 'secret') renderSecret();
    else renderSecretary();
  }

  // ---------------- 朝议 ----------------

  function renderCuria() {
    var d = state.data;
    var a = null;
    if (state.selectedId) {
      a = (d.history || []).filter(function (x) { return x.id === state.selectedId; })[0] || null;
    } else {
      a = d.active || null;
    }

    if (a) {
      if (state.selectedId) {
        var back = el('div', 'btn-row');
        var b = el('button', 'btn ghost', '← 返回当前朝议');
        b.addEventListener('click', function () { state.selectedId = null; renderAll(); });
        back.appendChild(b);
        viewEl.appendChild(back);
      }
      viewEl.appendChild(renderAgenda(a, a.id === (d.active && d.active.id)));
    } else {
      viewEl.appendChild(el('div', 'empty', '当前没有进行中的朝议。'));
      if (d.player && d.player.canParticipate) viewEl.appendChild(renderProposeForm());
    }

    if (d.history && d.history.length) {
      var card = el('div', 'card');
      var head = el('div', 'card-head');
      var left = el('div');
      left.appendChild(el('div', 'card-kicker', 'ARCHIVE'));
      left.appendChild(el('h2', 'card-title', '朝议记录'));
      left.appendChild(el('div', 'card-sub', '已结束的议题与结果。点击查看详情。'));
      head.appendChild(left);
      card.appendChild(head);
      var list = el('div', 'history-list');
      d.history.forEach(function (h) {
        var item = el('div', 'history-item');
        var t = el('div', 'h-title', h.typeLabel + ' · ' + h.target);
        var m = el('div', 'h-meta', h.statusLabel + ' · ' + (h.outcome || ''));
        item.appendChild(t);
        item.appendChild(m);
        item.addEventListener('click', function () {
          state.selectedId = h.id;
          renderAll();
        });
        list.appendChild(item);
      });
      card.appendChild(list);
      viewEl.appendChild(card);
    }
  }

  function renderAgenda(a, isActive) {
    var d = state.data;
    var card = el('div', 'card');

    var head = el('div', 'card-head');
    var left = el('div');
    left.appendChild(el('div', 'card-kicker', a.kindLabel + ' · ' + (isActive ? '正在进行' : '已结束')));
    left.appendChild(el('h2', 'card-title', a.typeLabel + ' · ' + a.target));
    var sub = el('div', 'card-sub');
    sub.textContent = '提议者：' + a.proposer + '　' + a.created + '　截止：第' + a.deadline + '日';
    left.appendChild(sub);
    head.appendChild(left);
    var badge = el('span', 'badge ' + statusClass(a.status), a.statusLabel);
    head.appendChild(badge);
    card.appendChild(head);

    var reasonField = el('div', 'field');
    reasonField.appendChild(el('div', 'field-label', '议题理由'));
    reasonField.appendChild(el('div', 'field-value reason', a.reason || '（未写理由）'));
    card.appendChild(reasonField);

    if (a.terms) {
      var terms = el('div', 'field');
      terms.appendChild(el('div', 'field-label', '条款 / 预期效果'));
      terms.appendChild(el('div', 'field-value', a.terms));
      card.appendChild(terms);
    }
    if (a.policyDescription) {
      var pd = el('div', 'field');
      pd.appendChild(el('div', 'field-label', '政策说明'));
      pd.appendChild(el('div', 'field-value', a.policyDescription));
      card.appendChild(pd);
    }
    if (a.policyEffect) {
      var pe = el('div', 'field');
      pe.appendChild(el('div', 'field-label', '政策效果'));
      pe.appendChild(el('div', 'field-value reason', a.policyEffect));
      card.appendChild(pe);
    }
    if (a.policyEffects) {
      var pfx = el('div', 'field');
      pfx.appendChild(el('div', 'field-label', '当前政策对朝议的修正'));
      pfx.appendChild(el('div', 'field-value reason', a.policyEffects));
      card.appendChild(pfx);
    }
    if (a.kingMissingReason) {
      var km = el('div', 'field');
      km.appendChild(el('div', 'field-label', '国王未参与'));
      km.appendChild(el('div', 'field-value reason', a.kingMissingReason));
      card.appendChild(km);
    }

    card.appendChild(el('div', 'divider'));
    card.appendChild(renderVoteBar(a));

    if (a.targetDefense) {
      var defense = el('div', 'field');
      defense.appendChild(el('div', 'field-label', '被针对家族陈情' + (a.targetDefenseBy ? ' · ' + a.targetDefenseBy : '')));
      defense.appendChild(el('div', 'field-value reason', a.targetDefense));
      card.appendChild(defense);
    }

    if (a.kingStance) {
      var king = el('div', 'field');
      king.appendChild(el('div', 'field-label', '国王态度'));
      var kt = a.kingStance === 'support' ? '支持' : a.kingStance === 'oppose' ? '反对' : '弃权';
      king.appendChild(el('div', 'field-value', kt + (a.kingReason ? '：' + a.kingReason : '')));
      card.appendChild(king);
    }

    card.appendChild(el('div', 'divider'));
    card.appendChild(renderVotesTable(a));

    if (a.absent && a.absent.length) {
      var absent = el('div', 'field');
      absent.appendChild(el('div', 'field-label', '未出席'));
      var txt = a.absent.map(function (x) { return x.clan + '（' + x.leader + '，' + x.reason + '）'; }).join('、');
      absent.appendChild(el('div', 'field-value small muted', txt));
      card.appendChild(absent);
    }

    if (a.outcome || a.effect || a.failure) {
      card.appendChild(el('div', 'divider'));
      var result = el('div', 'field');
      result.appendChild(el('div', 'field-label', '结果'));
      if (a.outcome) result.appendChild(el('div', 'field-value reason', a.outcome));
      if (a.effect) result.appendChild(el('div', 'field-value', '执行：' + a.effect));
      if (a.failure) result.appendChild(el('div', 'field-value', '失效：' + a.failure));
      card.appendChild(result);
    }

    if (a.canVote || a.canDefend) {
      card.appendChild(el('div', 'divider'));
      var actions = el('div', 'btn-row');
      if (a.canDefend) {
        var defend = el('button', 'btn primary', '陈情');
        defend.addEventListener('click', function () {
          dispatch('defend', { agendaId: a.id });
          setStatus('请在弹窗中写下陈情…');
        });
        actions.appendChild(defend);
      }
      if (a.canVote) {
        [['support', '支持', 'good'], ['oppose', '反对', 'bad'], ['abstain', '弃权', 'ghost']].forEach(function (x) {
          var b = el('button', 'btn ' + x[2], x[1]);
          b.addEventListener('click', function () {
            dispatch('vote', { agendaId: a.id, stance: x[0], reason: '' });
            setStatus('已提交立场：' + x[1]);
          });
          actions.appendChild(b);
        });
        var sel = createDropdown([
          { value: 'support', label: '支持' },
          { value: 'oppose', label: '反对' },
          { value: 'abstain', label: '弃权' }
        ], 'support');
        actions.appendChild(sel.el);
        var rb = el('button', 'btn', '附理由表态');
        rb.addEventListener('click', function () {
          dispatch('voteReason', { agendaId: a.id, stance: sel.value });
          setStatus('请在弹窗中写下理由…');
        });
        actions.appendChild(rb);
      }
      if (a.playerStanceLabel) {
        actions.appendChild(el('span', 'small muted', '你的立场：' + a.playerStanceLabel + (a.playerAutoSupport ? '（提议者默认）' : '') + (a.playerReason ? '（' + a.playerReason + '）' : '')));
      }
      card.appendChild(actions);
    } else if (a.playerStanceLabel) {
      var own = el('div', 'field-value small muted', '你的立场：' + a.playerStanceLabel + (a.playerReason ? '（' + a.playerReason + '）' : ''));
      card.appendChild(own);
    }

    return card;
  }

  function renderVoteBar(a) {
    var wrap = el('div', 'bar-wrap');
    var bar = el('div', 'bar');
    var total = Number(a.supportWeight || 0) + Number(a.opposeWeight || 0) + Number(a.abstainWeight || 0);
    var denom = total > 0 ? total : 1;
    var sup = el('div', 'seg-support');
    sup.style.width = (Number(a.supportWeight || 0) / denom * 100) + '%';
    var opp = el('div', 'seg-oppose');
    opp.style.width = (Number(a.opposeWeight || 0) / denom * 100) + '%';
    var abst = el('div', 'seg-abstain');
    abst.style.width = (Number(a.abstainWeight || 0) / denom * 100) + '%';
    bar.appendChild(sup); bar.appendChild(opp); bar.appendChild(abst);
    wrap.appendChild(bar);

    var legend = el('div', 'bar-legend');
    function leg(cls, text) {
      var s = el('span');
      s.appendChild(el('span', 'dot ' + cls));
      s.appendChild(document.createTextNode(text));
      return s;
    }
    legend.appendChild(leg('support', '支持 ' + fmt(a.supportWeight)));
    legend.appendChild(leg('oppose', '反对 ' + fmt(a.opposeWeight)));
    legend.appendChild(leg('abstain', '弃权 ' + fmt(a.abstainWeight)));
    legend.appendChild(leg('quorum', '门槛 ' + pct(a.thresholdPct) + ' · 支持率 ' + pct(a.supportPct)));
    wrap.appendChild(legend);

    var meta = el('div', 'hint-inline');
    meta.textContent = '应召权重 ' + fmt(a.summonedWeight) + ' / quorum ' + fmt(a.quorumWeight) +
      '（全国 ' + fmt(a.totalWeight) + '）' + (a.quorumMet ? ' · quorum 已足' : ' · quorum 不足') +
      (a.daysLeft > 0 && !a.isTerminal ? ' · 还剩 ' + a.daysLeft + ' 天' : '');
    wrap.appendChild(meta);
    return wrap;
  }

  function renderVotesTable(a) {
    var wrap = el('div');
    var title = el('div', 'field-label', '应召家族与立场');
    wrap.appendChild(title);
    var table = el('table', 'vote-table');
    var thead = el('thead');
    var trh = el('tr');
    ['家族 / 领袖', '权重', '立场', '理由', '应召原因'].forEach(function (h) {
      trh.appendChild(el('th', '', h));
    });
    thead.appendChild(trh);
    table.appendChild(thead);
    var tbody = el('tbody');
    (a.votes || []).forEach(function (v) {
      var tr = el('tr');
      var who = el('td');
      var name = v.clan + '（' + v.leader + '）';
      if (v.isPlayer) name += ' · 你';
      if (v.isKing) name += ' · 国王';
      if (v.isProposer) name += ' · 提议者';
      who.textContent = name;
      tr.appendChild(who);
      tr.appendChild(el('td', 'num', fmt(v.weight)));
      var st = el('td');
      var cls = v.stance ? v.stance : 'none';
      st.appendChild(el('span', 'stance ' + cls, v.stanceLabel || '未表态'));
      tr.appendChild(st);
      tr.appendChild(el('td', 'small', v.reason || '—'));
      tr.appendChild(el('td', 'small muted', v.summonReason || ''));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    wrap.appendChild(table);
    return wrap;
  }

  function newOwnerOptionsData(clans, excludeClanName) {
    var options = [];
    (clans || []).forEach(function (c) {
      if (!c.leaderEntityId) return;
      if (excludeClanName && c.name === excludeClanName) return;
      options.push({ value: c.leaderEntityId, label: c.name + ' · ' + c.leader });
    });
    return options;
  }

  function transferableFiefs(fiefs, clans) {
    return (fiefs || []).filter(function (f) {
      return newOwnerOptionsData(clans, f.ownerClan).length > 0;
    });
  }

  function availableProposeTypes(d) {
    if (!d.player || !d.player.canParticipate) return [];
    var types = [];
    var ks = d.kingdoms || [];
    if (ks.some(function (k) { return !!k.canWar; })) types.push('declare_war');
    if (ks.some(function (k) { return !!k.canPeace; })) types.push('propose_peace');
    if (ks.some(function (k) { return !!k.canTrade; })) types.push('propose_trade');
    if (ks.some(function (k) { return !!k.canAlliance; })) types.push('propose_alliance');
    if (ks.some(function (k) { return !!k.canBreakAlliance; })) types.push('break_alliance');
    var policies = d.policies || [];
    if (policies.some(function (p) { return p.allowed && !p.active; })) types.push('enact_policy');
    if (policies.some(function (p) { return p.allowed && p.active; })) types.push('revoke_policy');

    var fiefs = d.player.isKing
      ? (d.fiefs && d.fiefs.vassal ? d.fiefs.vassal : [])
      : (d.fiefs && d.fiefs.own ? d.fiefs.own : []);
    if (transferableFiefs(fiefs, d.clans).length > 0) types.push('transfer_fief');
    if (d.player.isKing && (d.clans || []).some(function (c) { return !c.isRulerClan; })) types.push('expel_clan');
    return types;
  }

  function policyOptionsForType(d, type) {
    return (d.policies || []).filter(function (p) {
      return p.allowed && (type === 'enact_policy' ? !p.active : !!p.active);
    }).map(function (p) {
      return { value: p.id, label: p.name };
    });
  }

  function kingdomOptionsForType(d, type) {
    return (d.kingdoms || []).filter(function (k) {
      if (type === 'declare_war') return !!k.canWar;
      if (type === 'propose_peace') return !!k.canPeace;
      if (type === 'propose_trade') return !!k.canTrade;
      if (type === 'propose_alliance') return !!k.canAlliance;
      if (type === 'break_alliance') return !!k.canBreakAlliance;
      return false;
    }).map(function (k) {
      return {
        value: k.name,
        label: k.name + (k.atWar ? '（交战中）' : '') + (k.hasTrade ? '（有贸易）' : '') + (k.isAlly ? '（同盟）' : '')
      };
    });
  }

  function renderProposeForm() {
    var d = state.data;
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'NEW MOTION'));
    card.appendChild(el('h2', 'card-title', '提出新议题'));
    card.appendChild(el('div', 'card-sub', '提案会公开给所有应召家族与史官；通过后执行，不通过则无事发生。'));
    if (d.policyEffects) {
      card.appendChild(el('div', 'policy-reminder', '当前政策对朝议的修正：' + d.policyEffects));
    }

    var types = availableProposeTypes(d);
    if (!types.length) {
      card.appendChild(el('div', 'empty', '当前没有满足前提的议题。宣战需目标未交战；议和需正在交战；贸易需未交战且无贸易协定；结盟需未交战且未结盟；背盟需已有盟约；封地转让/驱逐还需有可用目标。'));
      return card;
    }

    var grid = el('div', 'form-grid');
    var typeField = el('div', 'field');
    typeField.appendChild(el('div', 'field-label', '议题类型'));
    var typeSel = createDropdown(types.map(function (t) { return { value: t, label: TYPE_LABEL[t] }; }), types[0]);
    typeField.appendChild(typeSel.el);
    grid.appendChild(typeField);

    var kField = el('div', 'field');
    kField.appendChild(el('div', 'field-label', '目标王国'));
    var kSel = createDropdown(kingdomOptionsForType(d, types[0]), '');
    kField.appendChild(kSel.el);
    grid.appendChild(kField);

    var allFiefs = d.player && d.player.isKing
      ? (d.fiefs && d.fiefs.vassal ? d.fiefs.vassal : [])
      : (d.fiefs && d.fiefs.own ? d.fiefs.own : []);
    var fiefList = transferableFiefs(allFiefs, d.clans);

    var sField = el('div', 'field');
    sField.appendChild(el('div', 'field-label', '目标封地'));
    var sSel = createDropdown(fiefList.map(function (f) {
      return { value: f.name, label: f.name + '（' + f.ownerClan + '）' };
    }), '');
    sField.appendChild(sSel.el);
    grid.appendChild(sField);

    var nField = el('div', 'field');
    nField.appendChild(el('div', 'field-label', '新主家族领袖'));
    var nSel = createDropdown(newOwnerOptionsData(d.clans, fiefList.length ? fiefList[0].ownerClan : ''), '');
    nField.appendChild(nSel.el);
    grid.appendChild(nField);

    var cField = el('div', 'field');
    cField.appendChild(el('div', 'field-label', '目标家族（驱逐）'));
    var cSel = createDropdown((d.clans || []).filter(function (c) { return !c.isRulerClan; }).map(function (c) {
      return { value: c.name, label: c.name + ' · ' + c.leader };
    }), '');
    cField.appendChild(cSel.el);
    grid.appendChild(cField);

    var pField = el('div', 'field full');
    pField.appendChild(el('div', 'field-label', '政策'));
    var pSel = createDropdown(policyOptionsForType(d, types[0]), '');
    pField.appendChild(pSel.el);
    var pEffect = el('div', 'field-help policy-effect', '');
    pField.appendChild(pEffect);
    grid.appendChild(pField);

    function updatePolicyEffect() {
      var selected = null;
      var policies = d.policies || [];
      for (var i = 0; i < policies.length; i++) {
        if (String(policies[i].id) === String(pSel.value)) { selected = policies[i]; break; }
      }
      clear(pEffect);
      if (!selected) return;
      pEffect.appendChild(el('div', 'policy-effect-label', '原版效果'));
      pEffect.appendChild(el('div', 'policy-effect-text', selected.effect || '（游戏未提供说明）'));
      if (selected.description) pEffect.appendChild(el('div', 'policy-effect-desc', selected.description));
      if (selected.curiaEffect) {
        pEffect.appendChild(el('div', 'policy-effect-label curia', '朝议修正'));
        pEffect.appendChild(el('div', 'policy-effect-text curia', selected.curiaEffect));
      }
    }
    pSel.onChange = updatePolicyEffect;

    sSel.onChange = function () {
      var f = null;
      for (var i = 0; i < fiefList.length; i++) if (fiefList[i].name === sSel.value) { f = fiefList[i]; break; }
      nSel.setOptions(newOwnerOptionsData(d.clans, f ? f.ownerClan : ''), nSel.value);
    };

    card.appendChild(grid);
    var actions = el('div', 'form-actions');
    var submit = el('button', 'btn primary', '写理由并提交');
    actions.appendChild(submit);
    actions.appendChild(el('span', 'hint-inline', '提交后会弹出原生输入框填写理由。'));
    card.appendChild(actions);

    function updateFields() {
      var t = typeSel.value;
      var external = t === 'declare_war' || t === 'propose_peace' || t === 'propose_trade'
        || t === 'propose_alliance' || t === 'break_alliance';
      var isPolicy = t === 'enact_policy' || t === 'revoke_policy';
      kField.classList.toggle('hidden', !external);
      if (external) kSel.setOptions(kingdomOptionsForType(d, t), kSel.value);
      sField.classList.toggle('hidden', t !== 'transfer_fief');
      nField.classList.toggle('hidden', t !== 'transfer_fief');
      cField.classList.toggle('hidden', t !== 'expel_clan');
      pField.classList.toggle('hidden', !isPolicy);
      if (isPolicy) {
        pSel.setOptions(policyOptionsForType(d, t), pSel.value);
        updatePolicyEffect();
      }
    }
    typeSel.onChange = updateFields;
    updateFields();

    submit.addEventListener('click', function () {
      var payload = {
        type: typeSel.value,
        targetKingdom: kSel.value || '',
        settlementName: sSel.value || '',
        newOwnerEntityId: nSel.value || '',
        targetClanName: cSel.value || '',
        policyName: pSel.value || ''
      };
      dispatch('propose', payload);
      setStatus('请在弹窗中填写议题理由…');
    });
    return card;
  }

  // ---------------- 外交 ----------------

  function renderDiplomacy() {
    var d = state.data;
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'DIPLOMACY'));
    card.appendChild(el('h2', 'card-title', '外国提案'));
    card.appendChild(el('div', 'card-sub', '来自其他王国国王的提议。接受或拒绝会立即生效。'));
    if (!d.foreignProposals || !d.foreignProposals.length) {
      card.appendChild(el('div', 'empty', '暂无待处理的外国提案。'));
    } else {
      d.foreignProposals.forEach(function (p) {
        var f = el('div', 'field');
        f.appendChild(el('div', 'field-label', p.proposer + ' 提议' + p.typeLabel));
        if (p.message) f.appendChild(el('div', 'field-value reason', p.message));
        var row = el('div', 'btn-row');
        var yes = el('button', 'btn good', '接受');
        yes.addEventListener('click', function () {
          dispatch('respondProposal', { proposalId: p.id, accepted: true });
          setStatus('已接受提案…');
        });
        var no = el('button', 'btn bad', '拒绝');
        no.addEventListener('click', function () {
          dispatch('respondProposal', { proposalId: p.id, accepted: false });
          setStatus('已拒绝提案…');
        });
        row.appendChild(yes); row.appendChild(no);
        f.appendChild(row);
        card.appendChild(f);
      });
    }
    viewEl.appendChild(card);

    var card2 = el('div', 'card');
    card2.appendChild(el('div', 'card-kicker', 'STATE OF THE REALM'));
    card2.appendChild(el('h2', 'card-title', '当前外交'));
    var table = el('table', 'vote-table');
    var thead = el('thead');
    var trh = el('tr');
    ['王国', '状态', '贸易'].forEach(function (h) { trh.appendChild(el('th', '', h)); });
    thead.appendChild(trh); table.appendChild(thead);
    var tbody = el('tbody');
    (d.kingdoms || []).forEach(function (k) {
      var tr = el('tr');
      tr.appendChild(el('td', '', k.name));
      var st = k.atWar ? '交战中' : (k.isAlly ? '同盟' : '和平');
      tr.appendChild(el('td', '', st));
      tr.appendChild(el('td', '', k.hasTrade ? '有' : '无'));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody); card2.appendChild(table);
    viewEl.appendChild(card2);

    if (d.player && d.player.isKing) {
      var card3 = el('div', 'card');
      card3.appendChild(el('div', 'card-kicker', 'ENVOYS'));
      card3.appendChild(el('h2', 'card-title', '遣使问询'));
      card3.appendChild(el('div', 'card-sub', '问询与回复是公开外交记录，史官可读。'));
      var sendRow = el('div', 'btn-row');
      var kSel = createDropdown((d.kingdoms || []).map(function (k) {
        return { value: k.name, label: k.name };
      }), '');
      sendRow.appendChild(kSel.el);
      var send = el('button', 'btn', '遣使问询');
      send.addEventListener('click', function () {
        dispatch('consultSend', { targetKingdom: kSel.value });
        setStatus('请在弹窗中写问询内容…');
      });
      sendRow.appendChild(send);
      card3.appendChild(sendRow);

      if (d.consultThreads && d.consultThreads.length) {
        d.consultThreads.forEach(function (t) {
          var f = el('div', 'field');
          f.appendChild(el('div', 'field-label', t.label));
          f.appendChild(el('div', 'field-value small', t.body));
          var row = el('div', 'btn-row');
          var reply = el('button', 'btn', '回复');
          reply.addEventListener('click', function () {
            var parts = String(t.label || '').split('↔');
            var other = parts.length === 2 ? (parts[0].trim() === d.kingdom ? parts[1].trim() : parts[0].trim()) : '';
            if (!other) {
              setStatus('无法解析问询对象', true);
              return;
            }
            dispatch('consultReply', { targetKingdom: other });
            setStatus('请在弹窗中写回复…');
          });
          row.appendChild(reply);
          f.appendChild(row);
          card3.appendChild(f);
        });
      } else {
        card3.appendChild(el('div', 'empty', '暂无问询记录。'));
      }
      viewEl.appendChild(card3);
    }
  }

  // ---------------- 封地 ----------------

  function renderFiefs() {
    var d = state.data;
    var own = (d.fiefs && d.fiefs.own) || [];
    var vassal = (d.fiefs && d.fiefs.vassal) || [];
    var isKing = d.player && d.player.isKing;

    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'ROYAL DOMAIN'));
    card.appendChild(el('h2', 'card-title', isKing ? '我的封地与封臣封地' : '我的封地'));
    card.appendChild(el('div', 'card-sub', isKing
      ? '统治者可自由转让自己家族的封地；转让/剥夺封臣封地需经朝议。'
      : '封臣转让自己的封地也需经朝议；新主须是本国封臣家族领袖。'));

    if (own.length) {
      var table = el('table', 'vote-table');
      var thead = el('thead'); var trh = el('tr');
      ['我的封地', '类型', '所属家族'].forEach(function (h) { trh.appendChild(el('th', '', h)); });
      thead.appendChild(trh); table.appendChild(thead);
      var tbody = el('tbody');
      own.forEach(function (f) {
        var tr = el('tr');
        tr.appendChild(el('td', '', f.name));
        tr.appendChild(el('td', '', f.type));
        tr.appendChild(el('td', '', f.ownerClan));
        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      card.appendChild(table);
    } else {
      card.appendChild(el('div', 'empty', '你的家族没有封地。'));
    }
    viewEl.appendChild(card);

    if (isKing && transferableFiefs(own, d.clans).length > 0) {
      viewEl.appendChild(renderOwnTransferForm(own, d.clans));
    }

    if (isKing && vassal.length) {
      var card2 = el('div', 'card');
      card2.appendChild(el('div', 'card-kicker', 'VASSAL FIEFS'));
      card2.appendChild(el('h2', 'card-title', '封臣封地'));
      card2.appendChild(el('div', 'card-sub', '夺封/再分配需提出朝议；被针对家族会陈情，2/3 通过后执行。'));
      var table2 = el('table', 'vote-table');
      var thead2 = el('thead'); var trh2 = el('tr');
      ['封地', '类型', '所属家族', '领袖'].forEach(function (h) { trh2.appendChild(el('th', '', h)); });
      thead2.appendChild(trh2); table2.appendChild(thead2);
      var tbody2 = el('tbody');
      vassal.forEach(function (f) {
        var tr = el('tr');
        tr.appendChild(el('td', '', f.name));
        tr.appendChild(el('td', '', f.type));
        tr.appendChild(el('td', '', f.ownerClan));
        tr.appendChild(el('td', '', f.ownerLeader));
        tbody2.appendChild(tr);
      });
      table2.appendChild(tbody2); card2.appendChild(table2);
      viewEl.appendChild(card2);
      if (transferableFiefs(vassal, d.clans).length > 0)
        viewEl.appendChild(renderVassalTransferForm(vassal, d.clans));
    }

    if (isKing && (d.clans || []).some(function (c) { return !c.isRulerClan; })) {
      viewEl.appendChild(renderExpelForm(d.clans));
    }

    if (!isKing && transferableFiefs(own, d.clans).length > 0) {
      viewEl.appendChild(renderVassalOwnTransferForm(own, d.clans));
    }
  }

  function newOwnerOptions(clans, excludeClanName) {
    return createDropdown(newOwnerOptionsData(clans, excludeClanName), '');
  }

  function renderOwnTransferForm(own, clans) {
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'PATRIMONY'));
    card.appendChild(el('h2', 'card-title', '转让自己的封地'));
    card.appendChild(el('div', 'card-sub', '统治者自由处置自己家族的封地，不需朝议。'));
    var choices = transferableFiefs(own, clans);
    if (!choices.length) {
      card.appendChild(el('div', 'empty', '没有可接收该封地的其他家族。'));
      return card;
    }
    var grid = el('div', 'form-grid');
    var sf = el('div', 'field'); sf.appendChild(el('div', 'field-label', '封地'));
    var sSel = createDropdown(choices.map(function (f) { return { value: f.name, label: f.name }; }), '');
    sf.appendChild(sSel.el); grid.appendChild(sf);
    var nf = el('div', 'field'); nf.appendChild(el('div', 'field-label', '新主'));
    var nSel = newOwnerOptions(clans, choices[0].ownerClan);
    sSel.onChange = function () {
      var f = null;
      for (var i = 0; i < choices.length; i++) if (choices[i].name === sSel.value) { f = choices[i]; break; }
      nSel.setOptions(newOwnerOptionsData(clans, f ? f.ownerClan : ''), nSel.value);
    };
    nf.appendChild(nSel.el); grid.appendChild(nf);
    card.appendChild(grid);
    var row = el('div', 'form-actions');
    var btn = el('button', 'btn primary', '写理由并转让');
    btn.addEventListener('click', function () {
      dispatch('transferOwnFief', { settlementName: sSel.value, newOwnerEntityId: nSel.value });
      setStatus('请在弹窗中写册封理由…');
    });
    row.appendChild(btn); card.appendChild(row);
    return card;
  }

  function renderVassalTransferForm(vassal, clans) {
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'MOTION · FIEF'));
    card.appendChild(el('h2', 'card-title', '提夺封/转让议程'));
    card.appendChild(el('div', 'card-sub', '国王提出，2/3 通过后执行；被针对家族会陈情，不投票。'));
    var choices = transferableFiefs(vassal, clans);
    if (!choices.length) {
      card.appendChild(el('div', 'empty', '没有可接收这些封地的其他家族。'));
      return card;
    }
    var grid = el('div', 'form-grid');
    var sf = el('div', 'field'); sf.appendChild(el('div', 'field-label', '封地'));
    var sSel = createDropdown(choices.map(function (f) { return { value: f.name, label: f.name + '（' + f.ownerClan + '）' }; }), '');
    sf.appendChild(sSel.el); grid.appendChild(sf);
    var nf = el('div', 'field'); nf.appendChild(el('div', 'field-label', '新主'));
    var nSel = newOwnerOptions(clans, choices[0].ownerClan);
    sSel.onChange = function () {
      var f = null;
      for (var i = 0; i < choices.length; i++) if (choices[i].name === sSel.value) { f = choices[i]; break; }
      nSel.setOptions(newOwnerOptionsData(clans, f ? f.ownerClan : ''), nSel.value);
    };
    nf.appendChild(nSel.el); grid.appendChild(nf);
    card.appendChild(grid);
    var row = el('div', 'form-actions');
    var btn = el('button', 'btn primary', '写理由并提议');
    btn.addEventListener('click', function () {
      dispatch('propose', {
        type: 'transfer_fief',
        settlementName: sSel.value,
        newOwnerEntityId: nSel.value,
        targetKingdom: '',
        targetClanName: ''
      });
      setStatus('请在弹窗中写夺封理由…');
    });
    row.appendChild(btn); card.appendChild(row);
    return card;
  }

  function renderVassalOwnTransferForm(own, clans) {
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'MOTION · FIEF'));
    card.appendChild(el('h2', 'card-title', '提议转让自己的封地'));
    card.appendChild(el('div', 'card-sub', '封臣转让自己的封地也需朝议，2/3 通过后执行。'));
    var choices = transferableFiefs(own, clans);
    if (!choices.length) {
      card.appendChild(el('div', 'empty', '没有可接收该封地的其他家族。'));
      return card;
    }
    var grid = el('div', 'form-grid');
    var sf = el('div', 'field'); sf.appendChild(el('div', 'field-label', '封地'));
    var sSel = createDropdown(choices.map(function (f) { return { value: f.name, label: f.name }; }), '');
    sf.appendChild(sSel.el); grid.appendChild(sf);
    var nf = el('div', 'field'); nf.appendChild(el('div', 'field-label', '新主'));
    var nSel = newOwnerOptions(clans, choices[0].ownerClan);
    sSel.onChange = function () {
      var f = null;
      for (var i = 0; i < choices.length; i++) if (choices[i].name === sSel.value) { f = choices[i]; break; }
      nSel.setOptions(newOwnerOptionsData(clans, f ? f.ownerClan : ''), nSel.value);
    };
    nf.appendChild(nSel.el); grid.appendChild(nf);
    card.appendChild(grid);
    var row = el('div', 'form-actions');
    var btn = el('button', 'btn primary', '写理由并提议');
    btn.addEventListener('click', function () {
      dispatch('propose', {
        type: 'transfer_fief',
        settlementName: sSel.value,
        newOwnerEntityId: nSel.value,
        targetKingdom: '',
        targetClanName: ''
      });
      setStatus('请在弹窗中写转让理由…');
    });
    row.appendChild(btn); card.appendChild(row);
    return card;
  }

  function renderExpelForm(clans) {
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'MOTION · EXPULSION'));
    card.appendChild(el('h2', 'card-title', '提议驱逐家族'));
    card.appendChild(el('div', 'card-sub', '国王提出，2/3 通过后家族带封地离开王国；被针对家族会陈情。'));
    var grid = el('div', 'form-grid');
    var cf = el('div', 'field'); cf.appendChild(el('div', 'field-label', '目标家族'));
    var cSel = createDropdown((clans || []).filter(function (c) { return !c.isRulerClan; }).map(function (c) {
      return { value: c.name, label: c.name + ' · ' + c.leader };
    }), '');
    cf.appendChild(cSel.el); grid.appendChild(cf);
    card.appendChild(grid);
    var row = el('div', 'form-actions');
    var btn = el('button', 'btn primary', '写理由并提议');
    btn.addEventListener('click', function () {
      dispatch('propose', {
        type: 'expel_clan',
        targetClanName: cSel.value,
        targetKingdom: '',
        settlementName: '',
        newOwnerEntityId: ''
      });
      setStatus('请在弹窗中写驱逐理由…');
    });
    row.appendChild(btn); card.appendChild(row);
    return card;
  }

  // ---------------- 密陈 ----------------

  function renderSecret() {
    var d = state.data;
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'PRIVATE COUNSEL'));
    card.appendChild(el('h2', 'card-title', '密陈'));
    if (d.player && d.player.isKing) {
      card.appendChild(el('div', 'card-sub', '只呈国王一人的密陈，不入史册。'));
      if (d.secretAdvisory) {
        var pre = el('pre', 'secret', d.secretAdvisory);
        card.appendChild(pre);
      } else {
        card.appendChild(el('div', 'empty', '今年暂无可读密陈。'));
      }
      var row = el('div', 'btn-row');
      var refresh = el('button', 'btn', '刷新');
      refresh.addEventListener('click', function () { dispatch('refresh', {}); });
      row.appendChild(refresh);
      card.appendChild(row);
    } else if (d.player && d.player.canParticipate) {
      card.appendChild(el('div', 'card-sub', '密陈只呈国王一人，史官与旁人不可读。'));
      var btn = el('button', 'btn primary', '写密陈');
      btn.addEventListener('click', function () {
        dispatch('secretAdvisory', {});
        setStatus('请在弹窗中写密陈…');
      });
      card.appendChild(btn);
    } else {
      card.appendChild(el('div', 'empty', '雇佣兵不参与王国密陈。'));
    }
    viewEl.appendChild(card);
  }

  // ---------------- 秘书 ----------------

  function renderSecretary() {
    var card = el('div', 'card');
    card.appendChild(el('div', 'card-kicker', 'SECRETARIAT · QUERY ONLY'));
    card.appendChild(el('h2', 'card-title', '秘书处（仅查询）'));
    card.appendChild(el('div', 'card-sub', '秘书处只回答世界局势、人物、封地、朝议记录、外交提案等查询；不再处理任何政治动作。'));
    var row = el('div', 'btn-row');
    var open = el('button', 'btn primary', '打开秘书查询');
    open.addEventListener('click', function () {
      dispatch('openSecretary', {});
    });
    row.appendChild(open);
    var letters = el('button', 'btn', '打开书信（O）');
    letters.addEventListener('click', function () {
      dispatch('openLetters', {});
    });
    row.appendChild(letters);
    card.appendChild(row);
    card.appendChild(el('div', 'hint-inline', '游说其他家族：按 O 写信；或前往对方处当面交谈。'));
    viewEl.appendChild(card);
  }

  // ---------------- boot ----------------

  window.__game = {
    render: function (data) {
      state.data = data || {};
      state.selectedId = null;
      renderAll();
    },
    setStatus: function (text) { setStatus(text); }
  };

  bindBridge().then(function (ok) {
    if (!ok) setStatus('浏览器预览：无 CefSharp 桥', true);
  });

  renderNav();
  renderAll();
})();
