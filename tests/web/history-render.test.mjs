// 无浏览器环境下的史书页渲染回归：用最小 DOM 桩执行 history.js，
// 验证 Markdown 分节/强调/引用/列表/错误态，以及谏言纯文本不被 Markdown 改写。
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";

const root = path.dirname(path.dirname(path.dirname(fileURLToPath(import.meta.url))));
const script = fs.readFileSync(path.join(root, "_Module", "WebUI", "history.js"), "utf8");

class FakeNode {
  constructor(tag) {
    this.tagName = tag;
    this.nodeType = 1;
    this.children = [];
    this.className = "";
    this.textContent = "";
    this.style = { setProperty() {} };
    this.classList = { add() {}, remove() {} };
    this.attrs = {};
    this.scrollTop = 0;
    this.scrollHeight = 0;
    this.clientHeight = 0;
  }

  appendChild(child) {
    this.children.push(child);
    child.parentNode = this;
    return child;
  }

  removeChild(child) {
    const index = this.children.indexOf(child);
    if (index >= 0) this.children.splice(index, 1);
    return child;
  }

  get firstChild() {
    return this.children[0] || null;
  }

  get childNodes() {
    return this.children;
  }

  setAttribute(name, value) {
    this.attrs[name] = value;
  }

  addEventListener() {}

  scrollIntoView() {}

  closest() {
    return null;
  }
}

const elements = new Map();
for (const id of [
  "catalogList", "catalogCount", "reader", "articleTitle", "articleMeta",
  "articleCategory", "articleYear", "mastDot", "articleNumber", "articleTotal",
  "articleBody", "articleEnd", "progressBar", "pageLabel", "status",
]) {
  elements.set(id, new FakeNode("div"));
}

const document = {
  getElementById(id) {
    return elements.get(id) || null;
  },
  createElement(tag) {
    return new FakeNode(tag);
  },
  createTextNode(text) {
    return { nodeType: 3, textContent: String(text) };
  },
  documentElement: { style: { setProperty() {} } },
  elementFromPoint() {
    return null;
  },
};

const window = { document };
const sandbox = {
  window,
  document,
  console,
  setTimeout,
  clearTimeout,
  requestAnimationFrame(callback) {
    return callback(0);
  },
};

vm.runInNewContext(script, sandbox, { filename: "history.js" });

const game = window.__game;
assert.ok(game && typeof game.showArticle === "function", "window.__game.showArticle exists");

function nodeText(node) {
  if (!node) return "";
  if (node.nodeType === 3) return node.textContent || "";
  let text = node.textContent || "";
  for (const child of node.children || []) text += nodeText(child);
  return text;
}

function findNodes(node, tag) {
  const found = [];
  if (node.tagName === tag) found.push(node);
  for (const child of node.children || []) found.push(...findNodes(child, tag));
  return found;
}

const body = elements.get("articleBody");

// 1) Markdown 史文
game.showArticle({
  index: 0,
  title: "第1084年编年史",
  format: "markdown",
  fontSize: 18,
  category: "编年史",
  year: 1084,
  position: 2,
  total: 6,
  body: [
    "## 春",
    "",
    "**加粗**与`代码`",
    "",
    "> 史官曰：好",
    "",
    "- 一",
    "- 二",
    "",
    "---",
  ].join("\n"),
});

assert.equal(elements.get("articleCategory").textContent, "编年史", "article category");
assert.equal(elements.get("articleYear").textContent, "第 1084 年", "article year");
assert.equal(elements.get("articleNumber").textContent, "02", "article position");
assert.equal(elements.get("articleTotal").textContent, "06", "article total");
assert.ok(elements.get("articleMeta").textContent.includes("字"), "article character count");
assert.ok(elements.get("pageLabel").textContent.includes("第 2 篇"), "page label");

assert.ok(findNodes(body, "h2").some((node) => nodeText(node) === "春"), "markdown heading");
assert.ok(nodeText(body).includes("加粗与代码"), "inline formatting text");
assert.ok(findNodes(body, "strong").some((node) => nodeText(node) === "加粗"), "bold node");
assert.ok(findNodes(body, "code").some((node) => nodeText(node) === "代码"), "code node");
assert.ok(findNodes(body, "blockquote").some((node) => nodeText(node).includes("史官曰：好")), "blockquote");
assert.equal(findNodes(body, "li").length, 2, "list items");
assert.ok(findNodes(body, "hr").length >= 1, "horizontal rule");
assert.ok(!nodeText(body).includes("**"), "no markdown bold markers in output");
assert.ok(!nodeText(body).includes("##"), "no markdown heading markers in output");

// 1b) 正文重复篇名（旧史文常见）时跳过首个标题块
game.showArticle({
  index: 0,
  title: "第1084年编年史",
  format: "markdown",
  fontSize: 18,
  body: "# 卡拉迪亚第1084年编年史\n\n## 春\n正文",
});
assert.ok(!nodeText(body).includes("卡拉迪亚第1084年编年史"), "duplicate title line skipped");
assert.ok(nodeText(body).includes("春"), "content after duplicate title rendered");

// 2) 谏言/诏令/密陈保持纯文本
game.showArticle({
  index: 1,
  title: "封臣谏言 · 南帝国 · 第1084年",
  format: "plain",
  fontSize: 18,
  body: "**不经渲染**的谏言\n\n> 保持原样",
});
assert.ok(nodeText(body).includes("**不经渲染**的谏言"), "plain text preserves markdown chars");
assert.ok(nodeText(body).includes("> 保持原样"), "plain text preserves quote chars");

// 3) 读取失败
game.showArticle({ index: 2, title: "空篇", format: "markdown", body: "", error: "文件不存在" });
assert.ok(nodeText(body).includes("读取失败：文件不存在"), "error message shown");

console.log("history renderer tests passed");
