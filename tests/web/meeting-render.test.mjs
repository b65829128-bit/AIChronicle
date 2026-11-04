// 执行真实页面脚本，验证共享报价、动态事项字段、跨版本/跨会话点击和不可信文本。
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
class Element {
  constructor(tag = "div") {
    this.tagName = tag; this.className = ""; this.children = []; this.events = {};
    this.style = {}; this.scrollHeight = 500; this.scrollTop = 0; this.clientHeight = 300; this._text = "";
    this.classList = { add: c => { this.className += " " + c; }, remove: c => { this.className = this.className.split(" ").filter(x => x !== c).join(" "); } };
  }
  set textContent(value) { this._text = String(value ?? ""); this.children = []; }
  get textContent() { return this._text + this.children.map(c => c.textContent).join(""); }
  appendChild(child) { child.parentElement = this; this.children.push(child); return child; }
  replaceChildren(...children) {
    this.children = children; this._text = ""; this.scrollTop = 0;
    if (this.id === "deal" && this.parentElement) this.parentElement.scrollTop = 0;
  }
  addEventListener(name, callback) { this.events[name] = callback; }
  click() { this.events.click?.(); }
  remove() { this.parentElement.children = this.parentElement.children.filter(x => x !== this); }
  querySelector(selector) { return all(this).find(e => selector.startsWith(".") ? e.className.split(" ").includes(selector.slice(1)) : e.tagName === selector) || null; }
}
function all(el) { return el.children.flatMap(c => [c, ...all(c)]); }
const elements = new Map();
for (const id of fs.readFileSync(path.join(root, "_Module/WebUI/meeting.html"), "utf8").matchAll(/id="([^"]+)"/g)) {
  const element = new Element(); element.id = id[1]; elements.set(id[1], element);
}
const dealScroller = new Element("section"); dealScroller.appendChild(elements.get("deal"));
const sent = [];
let time = 1000, hovered = null, frameId = 0;
const frames = new Map();
const window = { aiChronicle: { dispatch: (id, payload) => sent.push({ id, ...JSON.parse(payload) }) } };
const document = { getElementById: id => elements.get(id), createElement: tag => new Element(tag), body: new Element("body"), elementFromPoint: () => hovered };
vm.runInNewContext(fs.readFileSync(path.join(root, "_Module/WebUI/meeting.js"), "utf8"), {
  window, document, console, Map, JSON, Number, performance: { now: () => time },
  requestAnimationFrame: callback => { frames.set(++frameId, callback); return frameId; },
  getComputedStyle: element => ({ overflowY: element.style.overflowY || "visible" })
});
function frame(count = 1) {
  for (let i = 0; i < count; i++) { time += 33; const pending = [...frames.values()]; frames.clear(); pending.forEach(callback => callback(time)); }
}
const game = window.__game;
const button = (scope, text) => {
  const match = all(elements.get(scope)).find(e => e.tagName === "button" && e.textContent.startsWith(text));
  assert.ok(match, `missing button: ${text}`); return match;
};
const base = {
  sessionId: "meeting-a", otherName: "阿尔文", yourName: "艾琳", yourClan: "橡木", yourTitle: "家族领袖", relation: 10,
  otherGold: 12000, yourGold: 3000,
  otherInventory: [{ id: "horse||", name: "骏马", count: 2, referenceValue: 300 }],
  yourInventory: [{ id: "sword|fine|", name: "精制长剑", count: 1, referenceValue: 600 }],
  inventoryHint: "物品保留品质。", busy: false, messages: [], request: null,
  types: [
    { id: "trade", title: "交易与赠礼", description: "商量报价", unavailable: "", fields: [] },
    { id: "marriage", title: "家族婚配", description: "可以无附加财物", unavailable: "", fields: [
      { key: "otherHeroId", label: "阿尔文家族人选", kind: "choice", choices: [{ id: "hero-a", label: "阿尔文" }] },
      { key: "yourHeroId", label: "艾琳家族人选", kind: "choice", choices: [{ id: "hero-b", label: "蕾娅" }] }
    ] },
    { id: "duel", title: "请求决斗", description: "", unavailable: "尚未开放", fields: [] }
  ]
};
game.render(structuredClone(base));
assert.match(elements.get("meetingTitle").textContent, /艾琳/);
assert.equal(button("actionList", "请求决斗").disabled, true);
button("actionList", "交易与赠礼").click();
button("deal", "0").click();
const numberRequest = sent.at(-1);
assert.equal(numberRequest.id, "number"); assert.equal(numberRequest.sessionId, "meeting-a");
game.setNumber({ editorToken: numberRequest.editorToken, path: numberRequest.path, value: 500 });
button("deal", "＋ 添加物品").click(); button("deal", "骏马").click();
button("deal", "1").click();
const quantityRequest = sent.at(-1);
assert.equal(quantityRequest.path, "otherOffer.item:horse||");
game.setNumber({ editorToken: quantityRequest.editorToken, path: quantityRequest.path, value: 2 });
button("deal", "提出这些条件").click();
let proposal = sent.at(-1);
assert.equal(proposal.id, "propose"); assert.equal(proposal.terms.otherOffer.gold, 500);
assert.equal(proposal.terms.otherOffer.items[0].id, "horse||"); assert.equal(proposal.terms.yourOffer.gold, 0);
assert.equal(proposal.terms.otherOffer.items[0].count, 2);

button("actionList", "家族婚配").click();
game.setNumber({ editorToken: numberRequest.editorToken, path: "otherOffer.gold", value: 9000 });
button("deal", "请选择").click(); button("deal", "阿尔文").click();
const stalePreview = sent.at(-1);
button("deal", "请选择").click(); button("deal", "蕾娅").click();
const currentPreview = sent.at(-1);
assert.equal(currentPreview.id, "preview");
game.preview({ editorToken: currentPreview.editorToken, revision: currentPreview.revision, text: "婚后归属：橡木家族" });
game.preview({ editorToken: stalePreview.editorToken, revision: stalePreview.revision, text: "旧人选预览" });
assert.match(elements.get("deal").textContent, /婚后归属：橡木家族/);
assert.doesNotMatch(elements.get("deal").textContent, /旧人选预览/);
button("deal", "提出这些条件").click(); proposal = sent.at(-1);
assert.equal(proposal.terms.otherOffer.gold, 0, "stale native-number callback cannot change a new draft");
assert.equal(proposal.terms.parameters.otherHeroId, "hero-a"); assert.equal(proposal.terms.parameters.yourHeroId, "hero-b");
assert.equal(proposal.terms.yourOffer.items.length, 0, "unconditional marriage uses empty offers");

const aiProposal = structuredClone(base);
aiProposal.request = { id: "offer-a", version: 1, status: 0, proposedBy: 1, otherAccepted: false, yourAccepted: true,
  terms: proposal.terms, reason: "<img src=x onerror=bad()>请考虑", result: "" };
aiProposal.messages = [{ sender: "艾琳", role: "assistant", text: "<script>bad()</script>", time: "1084春1" }];
game.render(aiProposal);
assert.equal(all(elements.get("messages")).some(e => e.tagName === "script"), false);
assert.equal(all(elements.get("deal")).some(e => e.tagName === "img"), false);
const oldAccept = button("deal", "接受这份约定");
const next = structuredClone(aiProposal); next.request.version = 2; next.request.terms.otherOffer.gold = 800;
game.render(next); oldAccept.click();
assert.equal(sent.at(-1).version, 1, "old click must carry old version so server can reject it");
button("deal", "接受这份约定").click(); assert.equal(sent.at(-1).version, 2);
button("deal", "修改条件").click(); button("deal", "提出修改后的条件").click();
assert.equal(sent.at(-1).requestId, "offer-a"); assert.equal(sent.at(-1).version, 2);
const oldSessionSubmit = button("deal", "提出修改后的条件");
const completed = structuredClone(next); completed.request.status = 2; completed.request.result = "婚配已生效。";
completed.types.find(t => t.id === "marriage").fields.forEach(f => { f.choices = []; });
game.render(completed);
assert.match(elements.get("deal").textContent, /蕾娅/); assert.doesNotMatch(elements.get("deal").textContent, /hero-b/);
const newSession = structuredClone(base); newSession.sessionId = "meeting-b";
newSession.types.push({ id: "new_action", title: "新注册事项", unavailable: "", description: "无需网页分支", fields: [
  { key: "target", label: "目标", kind: "choice", choices: [{ id: "target-1", label: "目标甲" }] },
  { key: "days", label: "天数", kind: "number", minimum: 0, maximum: 10 }
] });
game.render(newSession); const before = sent.length; oldSessionSubmit.click();
assert.equal(sent.length, before, "old session clicks cannot address a new meeting");
button("actionList", "新注册事项").click(); button("deal", "请选择").click(); button("deal", "目标甲").click();
button("deal", "填写").click();
const daysRequest = sent.at(-1); assert.equal(daysRequest.path, "parameters.days"); assert.equal(daysRequest.maximum, 10);
game.setNumber({ editorToken: daysRequest.editorToken, path: daysRequest.path, value: 7 });
button("deal", "提出这些条件").click();
assert.equal(sent.at(-1).terms.typeId, "new_action"); assert.equal(sent.at(-1).terms.parameters.target, "target-1");
assert.equal(sent.at(-1).terms.parameters.days, "7");
const busy = structuredClone(next); busy.busy = true; game.render(busy);
assert.equal(elements.get("speak").disabled, true); assert.equal(button("deal", "接受这份约定").disabled, true);
const compound = structuredClone(base); compound.sessionId = "compound";
const release = { id: "release_prisoner", title: "释放俘虏", description: "释放自己的指定俘虏。", unavailable: "", fields: [
  { key: "prisonerId", label: "释放谁", kind: "choice", choices: [{ id: "duke-a", label: "领主甲 ×1" }] },
  { key: "count", label: "人数", kind: "number", minimum: 1, maximum: 5 }
] };
compound.otherClauseTypes = [structuredClone(release)];
compound.yourClauseTypes = [structuredClone(release), {
  id: "change_allegiance", title: "家族加入／叛逃", description: "整个家族加入指定王国。", unavailable: "", fields: [
    { key: "kingdomId", label: "加入王国", kind: "choice", choices: [{ id: "kingdom-a", label: "白松王国" }] }
  ]
}];
game.render(compound); button("actionList", "交易与赠礼").click();
button("deal", "0").click(); const paymentInput = sent.at(-1);
game.setNumber({ editorToken: paymentInput.editorToken, path: paymentInput.path, value: 10000 });
button("deal", "＋ 添加我方行动").click(); button("deal", "释放俘虏").click();
button("deal", "请选择").click(); button("deal", "领主甲").click();
const prisonerCount = all(elements.get("deal")).find(e => e.tagName === "button" && e.textContent === "1"); prisonerCount.click();
const oldClauseInput = sent.at(-1);
button("deal", "移除").click();
button("deal", "＋ 添加我方行动").click(); button("deal", "释放俘虏").click();
button("deal", "请选择").click(); button("deal", "领主甲").click();
game.setNumber({ editorToken: oldClauseInput.editorToken, path: oldClauseInput.path, value: 99 });
button("deal", "＋ 添加对方行动").click(); button("deal", "家族加入／叛逃").click();
button("deal", "请选择").click(); button("deal", "白松王国").click();
button("deal", "提出这些条件").click();
const bundled = sent.at(-1);
assert.equal(bundled.terms.otherOffer.gold, 10000);
assert.equal(bundled.terms.otherOffer.actions[0].parameters.prisonerId, "duke-a");
assert.equal(bundled.terms.otherOffer.actions[0].parameters.count, "1", "old clause callback cannot change a newly added clause");
assert.equal(bundled.terms.yourOffer.actions[0].parameters.kingdomId, "kingdom-a", "allegiance action belongs to correct offer side");
const counter = structuredClone(compound);
counter.request = { id: "compound-offer", version: 2, status: 0, proposedBy: 1, otherAccepted: false, yourAccepted: true,
  terms: bundled.terms, reason: "同意这些条件", result: "" };
counter.request.terms.otherOffer.gold = 11000;
game.render(counter); button("deal", "接受这份约定").click();
assert.equal(sent.at(-1).version, 2, "compound counteroffer retains same version guard");
const partial = structuredClone(counter); partial.request.status = 6; partial.request.result = "已释放领主，加入王国未完成。";
game.render(partial);
assert.match(elements.get("deal").textContent, /部分履行/);
assert.equal(all(elements.get("deal")).some(e => e.tagName === "button" && e.textContent === "接受这份约定"), false);

const chat = elements.get("messages"); chat.scrollHeight = 2000; chat.clientHeight = 400; chat.style.overflowY = "auto"; chat.scrollTop = 0;
hovered = chat;
window.__scrollBySmooth(120, 0, 0); window.__scrollBySmooth(120, 0, 0);
frame(8); assert.ok(Math.abs(chat.scrollTop - 240) < 1, "wheel pixels accumulate without a second 0.45 reduction");
frame(3); assert.equal(chat.scrollTop, 240);
window.__scrollBySmooth(240, 0, 0); frame(1); const beforeReverse = chat.scrollTop;
window.__scrollBySmooth(-120, 0, 0); frame(1); assert.ok(chat.scrollTop < beforeReverse, "direction reversal reacts immediately"); frame(15);
const menu = new Element(); menu.style.overflowY = "auto"; menu.scrollHeight = 500; menu.clientHeight = 100; menu.parentElement = chat;
hovered = menu; const chatBeforeMenu = chat.scrollTop;
window.__scrollBySmooth(900, 0, 0); frame(20);
assert.equal(menu.scrollTop, 400, "nested menu clamps at its own boundary"); assert.equal(chat.scrollTop, chatBeforeMenu, "nested scrolling does not move conversation");
hovered = chat; time += 300; chat.scrollTop = 500; window.__scrollBySmooth(100, 0, 0); frame(20);
assert.equal(chat.scrollTop, 600, "new wheel burst starts at actual reading position");
chat.scrollTop = 125; dealScroller.scrollTop = 170;
game.render(partial);
assert.equal(chat.scrollTop, 125, "refresh preserves conversation reading position"); assert.equal(dealScroller.scrollTop, 170, "refresh preserves deal reading position");
console.log("meeting renderer: compound clauses, versioned offers, dynamic fields, safe text and responsive independent scrolling passed");
