(() => {
  "use strict";
  let data = {}, draft = null, selected = "", editing = false, editorToken = 0, note = "", seenRequest = "", session = "";
  let previewRevision = 0, previewText = "";
  const names = new Map();
  const fieldNames = new Map();
  const $ = id => document.getElementById(id);
  const copy = value => JSON.parse(JSON.stringify(value));
  const number = value => Number(value || 0).toLocaleString("zh-CN");
  const statuses = ["协商中", "执行中", "已成交", "已拒绝", "已撤回", "执行失败", "部分履行", "已约定，待开始", "进行中"];
  function node(tag, cls, text) {
    const el = document.createElement(tag);
    if (cls) el.className = cls;
    if (text !== undefined) el.textContent = text;
    return el;
  }
  function button(text, action, cls = "", disabled = false) {
    const el = node("button", "button " + cls, text);
    const renderedSession = data.sessionId;
    el.type = "button"; el.disabled = disabled;
    el.addEventListener("click", () => { if (!el.disabled && renderedSession === data.sessionId) action(); }); return el;
  }
  function dispatch(id, payload = {}) {
    if (!window.aiChronicle) return showError({ message: "当前无法送出请求。" });
    window.aiChronicle.dispatch(id, JSON.stringify({ ...payload, sessionId: data.sessionId }));
  }
  function showError(value) { $("error").textContent = value.message || ""; }
  function active() { return data.request && (data.request.status === 0 || data.request.status === 7); }
  function emptyOffer() { return { gold: 0, items: [], actions: [] }; }
  function blank(typeId) {
    const action = (data.types || []).find(a => a.id === typeId);
    return { typeId, parameters: {}, otherOffer: emptyOffer(), yourOffer: emptyOffer(),
      outcomes: (action?.outcomeSlots || []).map(slot => ({ id: slot.id, otherOffer: emptyOffer(), yourOffer: emptyOffer() })) };
  }
  function start(typeId) {
    if (data.busy || (active() && typeId !== data.request.terms.typeId)) return;
    selected = typeId;
    draft = active() ? copy(data.request.terms) : blank(typeId);
    editing = true; note = ""; previewText = ""; editorToken++;
    renderActions(); renderDeal();
    if (draft.outcomes?.length) requestPreview();
  }
  function requestPreview() {
    if (!draft || !editing) return;
    previewText = "";
    dispatch("preview", { editorToken: String(editorToken), revision: ++previewRevision, terms: draft });
  }
  function dropdown(label, choices, choose, disabled) {
    const outer = node("div", "dropdown");
    const toggle = button(label + " ▾", () => {
      const existing = outer.querySelector(".dropdown-menu");
      if (existing) { existing.remove(); outer.classList.remove("open"); return; }
      const menu = node("div", "dropdown-menu scrollable");
      for (const choice of choices) {
        const option = button(choice.label, () => { choose(choice.id); }, "dropdown-option", !!choice.disabled);
        menu.appendChild(option);
      }
      outer.classList.add("open"); outer.appendChild(menu);
    }, "dropdown-toggle quiet", disabled || !choices.length);
    outer.appendChild(toggle); return outer;
  }
  function renderActions() {
    const list = $("actionList"), previousTop = list.scrollTop; list.replaceChildren();
    const talk = button("", () => { selected = ""; editing = false; renderActions(); renderDeal(); }, "action" + (!selected ? " active" : ""));
    talk.appendChild(node("span", "action-title", "随意交谈"));
    talk.appendChild(node("span", "action-detail", "叙旧、问候，谈一谈近来的事。")); list.appendChild(talk);
    for (const action of data.types || []) {
      const locked = data.request?.status === 8 || active() && data.request.terms.typeId !== action.id;
      const el = button("", () => start(action.id), "action" + (selected === action.id ? " active" : ""), !!action.unavailable || locked || data.busy);
      el.appendChild(node("span", "action-title", action.title));
      el.appendChild(node("span", "action-detail", action.unavailable || (locked ? "请先结束当前交涉" : action.description)));
      list.appendChild(el);
    }
    list.scrollTop = previousTop;
  }
  function renderMessages() {
    const box = $("messages");
    const previousTop = box.scrollTop;
    const scroll = scrollStates.get(box);
    const scrollingUp = scroll?.raf && scroll.target < previousTop;
    const nearBottom = !scrollingUp && box.scrollHeight - box.scrollTop - box.clientHeight < 80;
    const count = box.children.length;
    box.replaceChildren();
    if (!(data.messages || []).length) {
      const empty = node("div", "empty-conversation");
      empty.appendChild(node("span", "large", "一言为约"));
      empty.appendChild(node("span", "", "可以先聊聊来意，也可以从左侧选择一件要商议的事。谈妥的条件会列在右侧，双方认可后付诸行动。"));
      box.appendChild(empty);
    }
    for (const message of data.messages || []) {
      const entry = node("article", "message " + (message.role || "assistant"));
      const meta = node("div", "message-meta");
      meta.appendChild(node("span", "", message.sender)); meta.appendChild(node("time", "", message.time));
      entry.appendChild(meta); entry.appendChild(node("div", "message-text", message.text)); box.appendChild(entry);
    }
    box.scrollTop = nearBottom || count === 0 ? box.scrollHeight : previousTop;
    $("thinking").textContent = data.busy ? "对方正在斟酌……" : "";
    $("speak").disabled = !!data.busy;
  }
  function numberButton(value, path, maximum) {
    return button(number(value), () => dispatch("number", { editorToken: String(editorToken), path, maximum }), "small quiet", data.busy);
  }
  function renderOffer(parent, side, title, editable, branch = null, prefix = "") {
    const terms = branch || (editable ? draft : data.request.terms);
    const offer = terms[side];
    const inventory = side === "otherOffer" ? data.otherInventory || [] : data.yourInventory || [];
    const gold = side === "otherOffer" ? data.otherGold : data.yourGold;
    const card = node("div", "offer");
    const heading = node("div", "offer-title", title);
    heading.appendChild(node("span", "balance", "持有 " + number(gold) + " 金币")); card.appendChild(heading);
    const line = node("div", "gold-line"); line.appendChild(node("span", "coin", "金币"));
    line.appendChild(editable ? numberButton(offer.gold, prefix + side + ".gold", gold) : node("span", "gold-value", number(offer.gold)));
    card.appendChild(line);
    for (const item of offer.items) {
      const row = node("div", "item-row"); row.appendChild(node("span", "item-name", names.get(item.id) || item.id));
      const quantity = node("div", "quantity");
      const maximum = inventory.find(i => i.id === item.id)?.count || 0;
      if (editable) quantity.appendChild(button("−", () => {
        item.count--; if (item.count <= 0) offer.items = offer.items.filter(i => i !== item); renderDeal();
      }, "small quiet", data.busy));
      quantity.appendChild(editable ? numberButton(item.count, prefix + side + ".item:" + item.id, maximum) : node("span", "", "× " + item.count));
      if (editable) quantity.appendChild(button("＋", () => { item.count++; renderDeal(); }, "small quiet", data.busy || item.count >= maximum));
      row.appendChild(quantity); card.appendChild(row);
    }
    if (!offer.items.length) card.appendChild(node("div", "nothing", "无附加物品"));
    if (editable) {
      const choices = inventory.filter(i => !offer.items.some(x => x.id === i.id)).map(i => ({
        id: i.id, label: i.name + " ×" + i.count + " · 参考 " + number(i.referenceValue) + " 金币"
      }));
      card.appendChild(dropdown("＋ 添加物品", choices, id => { offer.items.push({ id, count: 1 }); renderDeal(); }, data.busy || offer.items.length >= 32));
    }
    renderClauses(card, side, offer, editable, prefix);
    parent.appendChild(card);
  }
  function clauseTypes(side) {
    const types = side === "otherOffer" ? data.otherClauseTypes || [] : data.yourClauseTypes || [];
    const typeId = editing ? draft?.typeId : data.request?.terms.typeId;
    return types.filter(c => !c.actionTypes?.length || c.actionTypes.includes(typeId));
  }
  function renderClauses(parent, side, offer, editable, prefix = "") {
    const clauses = offer.actions || [];
    if (clauses.length) parent.appendChild(node("div", "clause-heading", "承诺执行的行动"));
    clauses.forEach((clause, index) => {
      const definition = clauseTypes(side).find(c => c.id === clause.typeId);
      const block = node("div", "clause-block");
      const heading = node("div", "clause-title", (index + 1) + ". " + (definition?.title || "行动条款"));
      if (editable) heading.appendChild(button("移除", () => {
        offer.actions.splice(index, 1); editorToken++; requestPreview(); renderDeal();
      }, "small quiet", data.busy));
      block.appendChild(heading);
      if (definition) {
        block.appendChild(node("p", "hint", definition.description));
        renderParameterFields(block, definition, clause.parameters, editable, prefix + side + ".actions." + index + ".parameters.", side + "|" + definition.id);
      }
      parent.appendChild(block);
    });
    if (editable) parent.appendChild(dropdown(side === "otherOffer" ? "＋ 添加我方行动" : "＋ 添加对方行动",
      clauseTypes(side).map(c => ({ id: c.id, label: c.title + (c.unavailable ? " · " + c.unavailable : ""), disabled: !!c.unavailable })), id => {
        const definition = clauseTypes(side).find(c => c.id === id);
        const parameters = {};
        for (const field of definition.fields || []) if (field.kind === "number") parameters[field.key] = String(field.minimum ?? 0);
        offer.actions ??= []; offer.actions.push({ typeId: id, parameters });
        editorToken++; requestPreview(); renderDeal();
      }, data.busy || clauses.length >= 8));
  }
  function renderFields(parent, action, editable) {
    const terms = editable ? draft : data.request.terms;
    renderParameterFields(parent, action, terms.parameters, editable, "parameters.", action.id);
  }
  function renderParameterFields(parent, action, parameters, editable, prefix, cacheKey) {
    for (const field of action.fields || []) {
      const wrapper = node("div", "field"); wrapper.appendChild(node("span", "field-label", field.label));
      const value = parameters[field.key] || "";
      if (!editable) wrapper.appendChild(node("span", "", (field.choices || []).find(c => c.id === value)?.label
        || fieldNames.get(cacheKey + "|" + field.key + "|" + value) || value || "未选择"));
      else if (field.kind === "choice") wrapper.appendChild(dropdown((field.choices || []).find(c => c.id === value)?.label || "请选择", field.choices || [], id => {
        parameters[field.key] = id; requestPreview(); renderDeal();
      }, data.busy));
      else if (field.kind === "number") wrapper.appendChild(button(value || "填写", () => dispatch("number", {
        editorToken: String(editorToken), path: prefix + field.key, minimum: field.minimum ?? 0, maximum: field.maximum ?? 2147483647
      }), "quiet", data.busy));
      else wrapper.appendChild(button(value || "填写", () => dispatch("fieldText", { editorToken: String(editorToken), key: prefix + field.key, label: field.label }), "quiet", data.busy));
      parent.appendChild(wrapper);
    }
  }
  function renderDeal() {
    const scroller = $("deal").parentElement;
    const previousTop = scroller?.scrollTop || 0;
    renderDealContents();
    if (scroller) scroller.scrollTop = previousTop;
  }
  function renderDealContents() {
    const box = $("deal"); box.replaceChildren();
    const request = data.request;
    const typeId = editing ? draft?.typeId : request?.terms?.typeId;
    const action = (data.types || []).find(a => a.id === typeId);
    if (!action) {
      box.appendChild(node("div", "section-kicker", "当前交涉"));
      const empty = node("div", "empty-deal"); empty.appendChild(node("strong", "", "尚未提出约定"));
      empty.appendChild(node("span", "", "选择一项事务，列出双方条件。对方也能在交谈中主动提出请求，你可以接受、拒绝或继续商量。")); box.appendChild(empty); return;
    }
    const top = node("div", "deal-top"); top.appendChild(node("h2", "", action.title));
    const badge = editing ? "拟定条件" : statuses[request.status] || "交涉中";
    top.appendChild(node("span", "badge" + (request?.status === 2 && !editing ? " success" : ""), badge)); box.appendChild(top);
    box.appendChild(node("div", "deal-intro", editing ? action.description : "第 " + request.version + " 版 · " + (request.proposedBy === 0 ? "由你提出" : "由对方提出")));
    renderFields(box, action, editing);
    if (action.unavailable) box.appendChild(node("p", "hint", action.unavailable));
    if (editing) {
      const summary = node("div", "terms-summary draft-preview", previewText);
      summary.hidden = !previewText; box.appendChild(summary);
    }
    const terms = editing ? draft : request.terms;
    if (action.outcomeSlots?.length) {
      box.appendChild(node("p", "hint", "分别约定两种结果。只履行实际胜者对应的一组条件；不附条件也可以提出。"));
      for (const slot of action.outcomeSlots) {
        const index = (terms.outcomes || []).findIndex(o => o.id === slot.id);
        const branch = terms.outcomes?.[index];
        const card = node("section", "outcome-card");
        card.appendChild(node("h3", "outcome-title", slot.title + "时"));
        if (branch) {
          renderOffer(card, "otherOffer", "我方履行", editing, branch, "outcomes." + index + ".");
          renderOffer(card, "yourOffer", "对方履行", editing, branch, "outcomes." + index + ".");
        } else card.appendChild(node("p", "hint", "此结果的条件尚未填写。"));
        box.appendChild(card);
      }
    } else {
      renderOffer(box, "otherOffer", "我方提供", editing); renderOffer(box, "yourOffer", "对方提供", editing);
    }
    box.appendChild(node("p", "hint", data.inventoryHint));
    if (editing) {
      box.appendChild(button(note ? "附言：" + note : "＋ 附上一句话（可选）", () => dispatch("note", { editorToken: String(editorToken) }), "quiet note-button", data.busy));
      const buttons = node("div", "buttons");
      const submission = copy(draft), requestId = active() ? request.id : "", version = active() ? request.version : 0, reason = note;
      buttons.appendChild(button(active() ? "提出修改后的条件" : "提出这些条件", () => dispatch("propose", {
        terms: submission, requestId, version, reason
      }), "primary", data.busy || !!action.unavailable));
      if (request) buttons.appendChild(button("返回", () => { editing = false; renderDeal(); }, "quiet"));
      box.appendChild(buttons);
      box.appendChild(node("p", "deal-help", action.outcomeSlots?.length
        ? "提出即认可两种结果的全部条件。双方确认同一版后，由你准备开始；接受约定时不交付胜负条件。"
        : "提出即表示你认可这些条件。对方可以接受、拒绝或还价；双方认可同一版约定后执行。婚配、投族和自立均可不附财物。"));
    } else {
      if (data.description) box.appendChild(node("div", "terms-summary", data.description));
      if (request.reason) box.appendChild(node("p", "reason", request.reason));
      if (active()) {
        const buttons = node("div", "buttons");
        if (request.status === 7) buttons.appendChild(button(action.startLabel || "开始", () => dispatch("startDeferred", { requestId: request.id, version: request.version }), "primary", data.busy || !!action.unavailable));
        if (!request.otherAccepted) buttons.appendChild(button("接受这份约定", () => respond("accept", request), "primary", data.busy));
        buttons.appendChild(button("修改条件", () => start(request.terms.typeId), "quiet", data.busy));
        buttons.appendChild(button(request.proposedBy === 0 ? "撤回" : "拒绝", () => respond(request.proposedBy === 0 ? "withdraw" : "reject", request), "danger", data.busy));
        box.appendChild(buttons);
        box.appendChild(node("p", "deal-help", request.status === 7 ? "双方已确认。核对最终条件后开始；开始前仍可修改或取消。"
          : request.otherAccepted ? "你已认可这一版，等待对方回应。" : "这是对方的条件。你可以接受，也可以继续商量。"));
      }
      if (request.result) box.appendChild(node("div", "result" + (request.status === 5 || request.status === 6 ? " failed" : ""), request.result));
    }
  }
  function respond(decision, request) { dispatch("respond", { requestId: request.id, version: request.version, decision }); }
  function render(incoming) {
    if (!incoming || incoming.closed) return;
    if (session !== incoming.sessionId) { session = incoming.sessionId; selected = ""; draft = null; editing = false; seenRequest = ""; names.clear(); fieldNames.clear(); editorToken++; }
    data = incoming;
    for (const item of [...data.otherInventory || [], ...data.yourInventory || []]) names.set(item.id, item.name);
    for (const action of data.types || []) for (const field of action.fields || []) for (const choice of field.choices || [])
      fieldNames.set(action.id + "|" + field.key + "|" + choice.id, choice.label);
    for (const side of ["otherOffer", "yourOffer"]) for (const action of side === "otherOffer" ? data.otherClauseTypes || [] : data.yourClauseTypes || []) for (const field of action.fields || []) for (const choice of field.choices || [])
      fieldNames.set(side + "|" + action.id + "|" + field.key + "|" + choice.id, choice.label);
    const requestKey = data.request ? data.request.id + ":" + data.request.version + ":" + data.request.status : "";
    if (requestKey !== seenRequest) {
      seenRequest = requestKey; editorToken++; note = "";
      if (data.request) { selected = data.request.terms.typeId; draft = copy(data.request.terms); editing = false; }
    }
    $("meetingTitle").textContent = "与 " + data.yourName + " 会晤";
    $("meetingMeta").textContent = data.yourClan + " · " + data.yourTitle + "　｜　关系 " + data.relation;
    renderActions(); renderMessages(); renderDeal();
  }
  function setNumber(value) {
    if (!editing || String(editorToken) !== value.editorToken || !Number.isInteger(value.value)) return;
    const resolved = offerPath(value.path);
    if (resolved?.tail === "gold" && value.value >= 0) resolved.offer.gold = value.value;
    else if (value.path.startsWith("parameters.") || value.path.includes(".actions.")) setParameter(value.path, String(value.value));
    else if (resolved?.tail.startsWith("item:")) {
      const id = resolved.tail.slice(5), item = resolved.offer.items.find(i => i.id === id);
      if (item && value.value >= 0) { item.count = value.value; if (!item.count) resolved.offer.items = resolved.offer.items.filter(i => i !== item); }
    }
    requestPreview(); renderDeal();
  }
  function offerPath(path) {
    const match = /^(?:outcomes\.(\d+)\.)?(otherOffer|yourOffer)\.(.+)$/.exec(path);
    if (!match || !draft) return null;
    const root = match[1] === undefined ? draft : draft.outcomes?.[Number(match[1])];
    return root?.[match[2]] ? { offer: root[match[2]], side: match[2], tail: match[3] } : null;
  }
  function setParameter(path, value) {
    if (path.startsWith("parameters.")) {
      const key = path.slice(11);
      if ((data.types || []).find(a => a.id === draft.typeId)?.fields.some(f => f.key === key)) draft.parameters[key] = value;
      return;
    }
    const resolved = offerPath(path);
    const match = /^actions\.(\d+)\.parameters\.([a-zA-Z0-9_]+)$/.exec(resolved?.tail || "");
    if (!match) return;
    const clause = resolved.offer.actions?.[Number(match[1])];
    if (clause && clauseTypes(resolved.side).find(c => c.id === clause.typeId)?.fields.some(f => f.key === match[2])) clause.parameters[match[2]] = value;
  }
  function setField(value) {
    if (!editing || String(editorToken) !== value.editorToken) return;
    if (value.key === "__note") note = value.text || "";
    else setParameter(value.key, value.text || "");
    if (value.key !== "__note") requestPreview();
    renderDeal();
  }
  function preview(value) {
    if (!editing || String(editorToken) !== value.editorToken || value.revision !== previewRevision) return;
    previewText = value.text || "";
    // 预览回调只更新摘要，保留用户正在滚动的下拉菜单及其原 DOM/动画。
    const summary = $("deal").querySelector(".draft-preview"), scroller = $("deal").parentElement;
    const previousTop = scroller?.scrollTop || 0;
    if (summary) { summary.textContent = previewText; summary.hidden = !previewText; }
    if (scroller) scroller.scrollTop = previousTop;
  }
  window.__game = { render, setNumber, setField, preview, showError };
  const scrollStates = new WeakMap();
  window.__scrollBySmooth = (delta, x, y) => {
    let el = document.elementFromPoint(x, y);
    // 左栏边缘/标题也把滚轮交给事项列表，避免侧栏留白成为滚动死区。
    if (el?.closest?.(".actions-column")) el = $("actionList");
    while (el && el !== document.body) {
      const style = getComputedStyle(el);
      if (/(auto|scroll)/.test(style.overflowY) && el.scrollHeight > el.clientHeight) break;
      el = el.parentElement;
    }
    if (!el || el === document.body || !Number.isFinite(Number(delta))) return;
    const now = performance.now();
    let state = scrollStates.get(el);
    if (!state) { state = { target: el.scrollTop, raf: 0, lastInput: 0, lastFrame: now }; scrollStates.set(el, state); }
    // 宿主已将滚轮转换为像素；完整累加，不再次缩小或反复重启浏览器的 smooth 动画。
    if (now - state.lastInput > 180 || Math.sign(delta) !== Math.sign(state.target - el.scrollTop)) state.target = el.scrollTop;
    state.target = Math.max(0, Math.min(el.scrollHeight - el.clientHeight, state.target + Number(delta)));
    state.lastInput = now;
    if (state.raf) return;
    state.lastFrame = now;
    function step(time) {
      if (el.isConnected === false) { state.raf = 0; return; }
      state.target = Math.max(0, Math.min(el.scrollHeight - el.clientHeight, state.target));
      const difference = state.target - el.scrollTop;
      if (Math.abs(difference) < 0.5) { el.scrollTop = state.target; state.raf = 0; return; }
      const elapsed = Math.max(1, Math.min(50, time - state.lastFrame));
      state.lastFrame = time;
      el.scrollTop += difference * (1 - Math.exp(-elapsed / 38));
      state.raf = requestAnimationFrame(step);
    }
    state.raf = requestAnimationFrame(step);
  };
  $("speak").addEventListener("click", () => dispatch("speak"));
  if (window.CefSharp) window.CefSharp.BindObjectAsync("aiChronicle").catch(() => showError({ message: "暂时无法送出请求，请重新会晤。" }));
})();
