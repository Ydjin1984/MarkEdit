/**
 * MarkEdit for Windows - WebView2 bridge shim.
 *
 * The editor core (CoreEditor) is platform agnostic and talks to the host through
 * `window.webkit.messageHandlers.bridge.postMessage(...)`, which returns a promise.
 * macOS implements that with WKScriptMessageHandlerWithReply; on Windows we implement
 * the same contract on top of chrome.webview messaging.
 *
 * This file is injected with AddScriptToExecuteOnDocumentCreatedAsync, so it is in place
 * before any page script runs. That matters: CoreEditor decides whether it is running
 * inside a native host by checking `typeof window.webkit?.messageHandlers === 'object'`.
 */
(() => {
  'use strict';

  const webview = window.chrome && window.chrome.webview;
  if (!webview) {
    return;
  }

  const pending = new Map();
  let nextCallID = 0;

  window.webkit = window.webkit || {};
  window.webkit.messageHandlers = window.webkit.messageHandlers || {};
  window.webkit.messageHandlers.bridge = {
    postMessage(message) {
      return new Promise((resolve, reject) => {
        const callID = ++nextCallID;
        pending.set(callID, { resolve, reject });
        webview.postMessage(Object.assign({ __markedit: 'native', callID }, message));
      });
    },
  };

  async function invokeWebModule(path, message) {
    if (typeof window.webModules !== 'object' || window.webModules === null) {
      return undefined;
    }

    const parts = String(path).split('.');
    let target = window.webModules;
    for (const part of parts) {
      if (target === null || target === undefined) {
        return undefined;
      }

      target = target[part];
    }

    if (typeof target !== 'function') {
      return undefined;
    }

    return await target(message);
  }

  // MARK: - Keyboard shortcuts
  //
  // The WebView2 owns the keyboard while the editor has focus, so menu accelerators registered
  // with WPF never see these keys. Instead the host installs the shortcut table here and we
  // forward the matching combinations back as commands.

  const shortcuts = [];
  const state = { findBarOpen: false };

  window.__markeditInstallShortcuts = table => {
    shortcuts.length = 0;
    shortcuts.push(...(table || []));
  };

  window.__markeditSetState = patch => {
    Object.assign(state, patch || {});
  };

  function matches(event, shortcut) {
    if (event.ctrlKey !== !!shortcut.ctrl) {
      return false;
    }

    if (event.shiftKey !== !!shortcut.shift) {
      return false;
    }

    if (event.altKey !== !!shortcut.alt) {
      return false;
    }

    const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
    return key === shortcut.key;
  }

  function hasCompletionPopup() {
    const tooltip = document.querySelector('.cm-tooltip-autocomplete');
    return tooltip !== null;
  }

  document.addEventListener(
    'keydown',
    event => {
      // Escape closes the find bar, but only when the editor has nothing else to dismiss.
      if (event.key === 'Escape' && state.findBarOpen && !hasCompletionPopup()) {
        event.preventDefault();
        event.stopPropagation();
        webview.postMessage({ __markedit: 'command', command: 'findBar.close' });
        return;
      }

      for (const shortcut of shortcuts) {
        if (!matches(event, shortcut)) {
          continue;
        }

        event.preventDefault();
        event.stopPropagation();
        webview.postMessage({ __markedit: 'command', command: shortcut.command });
        return;
      }
    },
    true,
  );

  webview.addEventListener('message', async event => {
    const data = event.data;
    if (!data || typeof data !== 'object') {
      return;
    }

    if (data.__markedit === 'reply') {
      const entry = pending.get(data.callID);
      if (!entry) {
        return;
      }

      pending.delete(data.callID);
      if (data.error !== null && data.error !== undefined) {
        entry.reject(new Error(String(data.error)));
      } else {
        entry.resolve(data.result);
      }

      return;
    }

    if (data.__markedit === 'invoke') {
      let result;
      let error = null;
      try {
        result = await invokeWebModule(data.path, data.message);
      } catch (e) {
        error = e && e.message ? e.message : String(e);
      }

      webview.postMessage({ __markedit: 'invokeResult', callID: data.callID, result, error });
    }
  });
})();
