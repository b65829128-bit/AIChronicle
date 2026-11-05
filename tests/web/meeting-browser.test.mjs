// 本机 Chrome 布局/滚动回归，无 npm 依赖。CHROME_BIN 可覆盖浏览器路径。
// 单独执行：node tests/web/meeting-browser.test.mjs；CI 的 DOM 桩回归不要求安装 Chrome。
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";
import assert from "node:assert/strict";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const web = path.join(root, "_Module/WebUI");
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "aichronicle-meeting-browser-"));
const description = "由非家族领袖本人决定去向；配偶及同族未成年子女随迁，保留本人金钱与部队。可无偿商议或附双方财物；被俘时也可商议，转族本身不代表获释。";
const choices = Array.from({ length: 70 }, (_, i) => ({ id: "kingdom-" + i, label: "第" + i + "个测试王国" }));
const data = {
  sessionId: "real-browser", otherName: "阿尔文", yourName: "艾琳", yourClan: "橡木", yourTitle: "成员", relation: 5,
  otherGold: 10000, yourGold: 1000, otherInventory: [], yourInventory: [], request: null, inventoryHint: "双方自愿报价。",
  messages: Array.from({ length: 25 }, (_, i) => ({ sender: "艾琳", role: "assistant", time: "今日", text: "第" + i + "条往来。".repeat(35) })),
  types: [
    { id: "trade", title: "交易与赠礼", description: "组合金币、物资与行动条件：交换财物、释放俘虏，或商议家族转换阵营。", fields: [] },
    { id: "marriage", title: "家族婚配", description: "指定双方人选，提出无条件婚配，或附上双向金币、物资条件，与家族领袖协商。", fields: [] },
    { id: "duel", title: "请求决斗", description: "双方本人参战，分别约定胜负后的双向财物和行动。", fields: [] },
    { id: "join_clan", title: "加入家族", description, fields: [] },
    { id: "found_clan", title: "自立家族", description, fields: [
      { key: "familyId", label: "本人及随迁家庭", kind: "choice", choices: [{ id: "family-a", label: "艾琳及配偶、幼女" }] },
      { key: "clanName", label: "新家族名称", kind: "text" },
      { key: "kingdomId", label: "新家族归属", kind: "choice", choices }
    ] }
  ]
};

// 在真正的 Chromium DOM/CSS/命中测试与 requestAnimationFrame 上执行产品脚本。
const probe = `
const passed = [], sent = [];
window.aiChronicle = { dispatch: (id, payload) => sent.push({ id, ...JSON.parse(payload) }) };
const check = (condition, message) => { if (!condition) throw new Error(message); passed.push(message); };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const settle = async el => {
  let previous = el.scrollTop, stable = 0;
  for (let i = 0; i < 50; i++) {
    await sleep(20);
    stable = Math.abs(el.scrollTop - previous) < 0.1 ? stable + 1 : 0;
    previous = el.scrollTop;
    if (stable >= 3) return;
  }
  throw new Error('scroll animation did not settle');
};
const rect = el => el.getBoundingClientRect();
const wheel = (el, delta) => { const r = rect(el); window.__scrollBySmooth(delta, r.left + r.width / 2, r.top + Math.min(30, r.height / 2)); };
const clickText = (parent, text) => { const b = [...parent.querySelectorAll('button')].find(e => e.textContent.startsWith(text)); if (!b) throw new Error('button missing: ' + text); b.click(); };
async function run() {
  await document.fonts.ready;
  const data = ${JSON.stringify(data)};
  window.__game.render(data);
  const workspace = document.querySelector('.workspace'), nav = document.getElementById('actionList');
  const chat = document.getElementById('messages'), deal = document.getElementById('deal'), scroller = deal.parentElement;
  for (const selector of ['.actions-column', '.conversation', '.deal-column']) {
    check(rect(document.querySelector(selector)).bottom <= rect(workspace).bottom + 1, selector + ' stays inside workspace');
  }
  check(nav.scrollHeight > nav.clientHeight && getComputedStyle(nav).overflowY === 'auto', 'long actions have an independent viewport');
  wheel(nav, 10000); await settle(nav);
  check(nav.scrollTop > 100 && rect(nav.lastElementChild).bottom <= rect(nav).bottom + 1, 'wheel reaches the last new action');
  const marginTop = nav.scrollTop, sidebar = rect(document.querySelector('.actions-column'));
  window.__scrollBySmooth(-96, sidebar.left + 5, sidebar.top + 350); await settle(nav);
  check(nav.scrollTop < marginTop - 50, 'sidebar margin wheel reaches the action list');
  wheel(nav, 10000); await settle(nav);
  const actionTop = nav.scrollTop;
  clickText(nav, '自立家族');
  check(Math.abs(nav.scrollTop - actionTop) < 1, 'selecting an action retains menu position');
  check(scroller.scrollHeight > scroller.clientHeight, 'long deal has its own scrollbar');
  const fields = () => [...deal.querySelectorAll('.field')];
  fields()[0].querySelector('button').click(); clickText(fields()[0], '艾琳及配偶');
  const previewRequest = sent.at(-1);
  fields()[2].querySelector('button').click();
  const menu = fields()[2].querySelector('.dropdown-menu');
  wheel(menu, 96); await settle(menu);
  check(menu.scrollTop > 50 && scroller.scrollTop === 0, 'nested dropdown scroll does not move the deal');
  const menuTop = menu.scrollTop;
  window.__game.preview({ editorToken: previewRequest.editorToken, revision: previewRequest.revision, text: '将建立新家族，保留本人部队。' });
  check(menu.isConnected && fields()[2].querySelector('.dropdown-menu') === menu, 'delayed preview keeps the open menu');
  check(Math.abs(menu.scrollTop - menuTop) < 1, 'delayed preview keeps dropdown scroll position');
  fields()[2].querySelector('.dropdown-toggle').click();
  wheel(scroller, 192); await settle(scroller);
  check(scroller.scrollTop > 100, 'deal wheel stays usable with five long actions');
  const previousDeal = scroller.scrollTop;
  window.__game.render(data);
  check(Math.abs(scroller.scrollTop - previousDeal) < 1 && Math.abs(nav.scrollTop - actionTop) < 1,
    'data refresh preserves both panel positions: ' + JSON.stringify({ deal: scroller.scrollTop, previousDeal, nav: nav.scrollTop, actionTop }));
  chat.scrollTop = chat.scrollHeight;
  wheel(chat, -96);
  const oldTop = chat.scrollTop;
  const growing = structuredClone(data);
  growing.messages.push({ sender: '艾琳', role: 'assistant', time: '今日', text: '新回复。'.repeat(100) });
  window.__game.render(growing); await settle(chat);
  check(chat.scrollTop < oldTop && chat.scrollHeight - chat.clientHeight - chat.scrollTop > 80, 'streaming refresh respects upward reading intent');
}
run().then(() => output({ ok: true, passed })).catch(error => output({ ok: false, passed, error: error.message }));
function output(result) { const pre = document.createElement('pre'); pre.id = 'browser-report'; pre.textContent = JSON.stringify(result); document.body.append(pre); }
`;

let html = fs.readFileSync(path.join(web, "meeting.html"), "utf8")
  .replace('<link rel="stylesheet" href="meeting.css">', () => "<style>" + fs.readFileSync(path.join(web, "meeting.css"), "utf8") + "</style>")
  .replace('<script src="meeting.js" defer></script>', "");
html = html.replace("</body>", () => "<script>" + fs.readFileSync(path.join(web, "meeting.js"), "utf8") + "</script><script>" + probe + "</script></body>");
const fixture = path.join(tmp, "meeting.html"); fs.writeFileSync(fixture, html);
try {
  // 使用本地调试管道与真实时钟；dump-dom 的虚拟时间会跳过绘制帧，不能用于验证 RAF 动画。
  const browser = spawn(process.env.CHROME_BIN || "google-chrome-stable", [
    "--headless", "--no-sandbox", "--disable-gpu", "--disable-background-networking", "--no-first-run",
    "--disable-background-timer-throttling", "--disable-renderer-backgrounding", "--remote-debugging-pipe",
    "--hide-scrollbars", "--window-size=1360,800", "--user-data-dir=" + path.join(tmp, "profile"),
    "about:blank"
  ], { stdio: ["ignore", "ignore", "pipe", "pipe", "pipe"] });
  let sequence = 0, buffer = "", stderr = "";
  const pending = new Map();
  browser.stderr.on("data", chunk => { stderr = (stderr + chunk).slice(-4000); });
  browser.stdio[4].setEncoding("utf8");
  browser.stdio[4].on("data", chunk => {
    buffer += chunk;
    let end;
    while ((end = buffer.indexOf("\0")) >= 0) {
      const message = JSON.parse(buffer.slice(0, end)); buffer = buffer.slice(end + 1);
      const waiting = pending.get(message.id);
      if (waiting) {
        clearTimeout(waiting.timer); pending.delete(message.id);
        if (message.error) waiting.reject(new Error(JSON.stringify(message.error)));
        else waiting.resolve(message.result);
      }
    }
  });
  function command(method, params = {}, sessionId) {
    return new Promise((resolve, reject) => {
      const id = ++sequence;
      const timer = setTimeout(() => { pending.delete(id); reject(new Error(method + " timed out: " + stderr)); }, 10000);
      pending.set(id, { resolve, reject, timer });
      browser.stdio[3].write(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }) + "\0");
    });
  }
  try {
    await command("Browser.getVersion");
    const { targetId } = await command("Target.createTarget", { url: "about:blank" });
    const { sessionId } = await command("Target.attachToTarget", { targetId, flatten: true });
    await command("Page.enable", {}, sessionId);
    await command("Emulation.setDeviceMetricsOverride", { width: 1360, height: 800, deviceScaleFactor: 1, mobile: false }, sessionId);
    await command("Target.activateTarget", { targetId });
    await command("Page.navigate", { url: pathToFileURL(fixture).href }, sessionId);
    let text;
    for (let i = 0; i < 100; i++) {
      const value = await command("Runtime.evaluate", { expression: "document.getElementById('browser-report')?.textContent", returnByValue: true }, sessionId);
      text = value.result.value;
      if (text) break;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(text, "browser did not complete the layout checks");
    const report = JSON.parse(text);
    assert.equal(report.ok, true, JSON.stringify(report));
    console.log("meeting browser: " + report.passed.length + " real layout/scroll checks passed");
  } finally {
    await command("Browser.close").catch(() => {});
    browser.kill();
    for (const value of pending.values()) clearTimeout(value.timer);
  }
} finally { fs.rmSync(tmp, { recursive: true, force: true }); }
