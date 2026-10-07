// Pixel chat panel. Talks to the WPF host over chrome.webview messages.
// All model output is sanitised before it reaches the DOM: screen text can contain anything.
(() => {
  "use strict";

  const host = window.chrome && window.chrome.webview;
  const send = (msg) => host && host.postMessage(msg);

  const $ = (id) => document.getElementById(id);
  const log = $("log"), empty = $("empty"), text = $("text"), sendBtn = $("send");
  const statusEl = $("status"), seeing = $("seeing"), eye = $("eye");

  let busy = false;
  let current = null;      // { el, raw } of the assistant message being streamed
  let renderTimer = 0;

  marked.use({
    gfm: true,
    breaks: true,
    renderer: {
      html: (token) => escapeHtml(token.text ?? token.raw ?? ""), // never pass raw HTML through
    },
  });

  // ---------------------------------------------------------------- rendering

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  function renderMarkdown(el, raw, final) {
    const html = DOMPurify.sanitize(marked.parse(raw), {
      FORBID_TAGS: ["style", "img", "iframe", "form", "input", "button"],
      FORBID_ATTR: ["style"],
    });
    el.innerHTML = html;
    el.querySelectorAll("pre > code").forEach((code) => decorateCode(code, final));
    el.querySelectorAll("a").forEach((a) => {
      a.addEventListener("click", (e) => {
        e.preventDefault();
        send({ type: "openLink", href: a.getAttribute("href") || "" });
      });
    });
  }

  function decorateCode(code, final) {
    const pre = code.parentElement;
    const lang = ([...code.classList].find((c) => c.startsWith("language-")) || "").replace("language-", "");
    const box = document.createElement("div");
    box.className = "code";
    const head = document.createElement("header");
    const label = document.createElement("span");
    label.textContent = lang || "code";
    const copy = document.createElement("button");
    copy.textContent = "Copy";
    copy.addEventListener("click", () => {
      send({ type: "copy", text: code.textContent });
      copy.textContent = "Copied ✓";
      setTimeout(() => (copy.textContent = "Copy"), 1400);
    });
    head.append(label, copy);
    pre.replaceWith(box);
    box.append(head, pre);
    if (final) {
      try { hljs.highlightElement(code); } catch { /* unknown language: leave plain */ }
    }
  }

  function scrollDown() {
    log.scrollTop = log.scrollHeight;
  }

  function addUser(t) {
    empty.hidden = true;
    const row = document.createElement("div");
    row.className = "msg user";
    const b = document.createElement("div");
    b.className = "bubble";
    b.textContent = t;
    row.append(b);
    log.append(row);
    scrollDown();
  }

  function startBot() {
    empty.hidden = true;
    const row = document.createElement("div");
    row.className = "msg bot";
    const b = document.createElement("div");
    b.className = "bubble";
    b.innerHTML = '<span class="dots"><i></i><i></i><i></i></span>';
    row.append(b);
    log.append(row);
    current = { el: b, raw: "" };
    scrollDown();
  }

  function token(t) {
    if (!current) startBot();
    current.raw += t;
    if (!renderTimer) {
      renderTimer = setTimeout(() => {
        renderTimer = 0;
        if (!current) return;
        renderMarkdown(current.el, current.raw, false);
        current.el.classList.add("caret");
        scrollDown();
      }, 40);
    }
  }

  function done() {
    if (current) {
      clearTimeout(renderTimer);
      renderTimer = 0;
      if (current.raw.trim()) renderMarkdown(current.el, current.raw, true);
      else if (current.el.querySelector(".dots")) current.el.parentElement.remove();
      current.el.classList.remove("caret");
    }
    current = null;
    setBusy(false);
    scrollDown();
  }

  function error(t) {
    if (!current) startBot();
    current.el.innerHTML = "";
    const p = document.createElement("p");
    p.className = "error";
    p.textContent = t;
    current.el.append(p);
    current = null;
    setBusy(false);
  }

  function action(a) {
    if (!current) startBot();
    if (current.el.querySelector(".dots")) current.el.innerHTML = "";
    const card = document.createElement("div");
    card.className = "action";
    card.dataset.id = a.id;
    const title = document.createElement("div");
    title.className = "title";
    title.textContent = a.title;
    const detail = document.createElement("div");
    detail.className = "detail";
    detail.textContent = a.detail || "";
    card.append(title, detail);
    if (a.needsConfirm) {
      const buttons = document.createElement("div");
      buttons.className = "buttons";
      const yes = document.createElement("button");
      yes.className = "yes";
      yes.textContent = "Do it";
      const no = document.createElement("button");
      no.textContent = "No thanks";
      const answer = (ok) => {
        buttons.remove();
        send({ type: "confirm", id: a.id, ok });
      };
      yes.addEventListener("click", () => answer(true));
      no.addEventListener("click", () => answer(false));
      buttons.append(yes, no);
      card.append(buttons);
    }
    // Cards sit before the streamed text of this turn.
    current.el.parentElement.insertBefore(card, current.el);
    scrollDown();
  }

  function actionResult(r) {
    const card = log.querySelector(`.action[data-id="${CSS.escape(r.id)}"]`);
    if (!card) return;
    const res = document.createElement("div");
    res.className = "result" + (r.ok ? "" : " bad");
    res.textContent = r.text;
    card.append(res);
  }

  function reset() {
    log.querySelectorAll(".msg, .action").forEach((n) => n.remove());
    empty.hidden = false;
    current = null;
    setBusy(false);
    text.value = "";
    autosize();
  }

  // ---------------------------------------------------------------- composer

  function setBusy(b) {
    busy = b;
    sendBtn.disabled = b || !text.value.trim();
  }

  function autosize() {
    text.style.height = "auto";
    text.style.height = Math.min(text.scrollHeight, 120) + "px";
    sendBtn.disabled = busy || !text.value.trim();
  }

  function submit(value) {
    const t = (value ?? text.value).trim();
    if (!t || busy) return;
    addUser(t);
    startBot();
    setBusy(true);
    send({ type: "send", text: t, useScreen: eye.classList.contains("on") });
    text.value = "";
    autosize();
  }

  text.addEventListener("input", autosize);
  text.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
      e.preventDefault();
      submit();
    }
  });
  sendBtn.addEventListener("click", () => submit());
  document.querySelectorAll(".chip").forEach((c) => c.addEventListener("click", () => submit(c.textContent)));
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") send({ type: "close" });
  });
  $("close").addEventListener("click", () => send({ type: "close" }));
  $("newChat").addEventListener("click", () => send({ type: "newChat" }));
  eye.addEventListener("click", () => {
    const on = !eye.classList.contains("on");
    eye.classList.toggle("on", on);
    eye.setAttribute("aria-pressed", String(on));
    seeing.dataset.off = on ? "" : "1";
    seeing.textContent = on ? seeing.dataset.title || "Reading your screen" : "Not reading your screen";
  });
  $("drag").addEventListener("mousedown", (e) => {
    if (e.button === 0 && !e.target.closest("button")) send({ type: "drag" });
  });

  // ---------------------------------------------------------------- host messages

  if (host) {
    host.addEventListener("message", (e) => {
      const m = e.data || {};
      switch (m.type) {
        case "token": token(m.text || ""); break;
        case "done": done(); break;
        case "error": error(m.text || "Something went wrong."); break;
        case "status":
          statusEl.textContent = m.text || "here for you";
          statusEl.classList.toggle("busy", !!m.busy);
          break;
        case "context":
          seeing.dataset.title = m.text || "";
          if (!seeing.dataset.off) seeing.textContent = m.text || "Reading your screen";
          break;
        case "action": action(m); break;
        case "actionResult": actionResult(m); break;
        case "avatar": $("avatar").src = m.dataUrl; break;
        case "reset": reset(); break;
        case "focus": text.focus(); break;
      }
    });
    send({ type: "ready" });
  }

  autosize();
})();
