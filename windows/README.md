# MarkEdit for Windows

A Windows port of [MarkEdit](https://github.com/MarkEdit-app/MarkEdit), built on the very same
editor core. The macOS app pairs the CodeMirror 6 editing core with an AppKit host; this port pairs
that same core with a .NET/WPF host and the WebView2 runtime.

```
CoreEditor (TypeScript, CodeMirror 6)  ->  dist/index.html (single self-contained file)
        |                                              |
        |  bridge: { moduleName, methodName, params }  |
        v                                              v
macOS host (Swift/AppKit, WKWebView)      Windows host (C#/WPF, WebView2)
```

## Requirements

| | |
| --- | --- |
| To run | Windows 10 1809 or later, [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on Windows 11 and on most Windows 10 machines) |
| To build | .NET SDK 10, Node.js 22 or later |

## Building

```cmd
cd windows
build.cmd
```

The script builds `CoreEditor` first, then publishes the app to `dist\windows`. Pass `framework` to
produce a small framework-dependent build instead of the self-contained single file:

```cmd
build.cmd framework
```

## Running

```cmd
dist\windows\MarkEdit.exe
dist\windows\MarkEdit.exe path\to\document.md
```

## Verifying the port

The app ships with a self-test that loads the real editor bundle, drives it through the bridge and
reports the result. It runs a hidden window, so it is safe to use in a build pipeline:

```cmd
dist\windows\MarkEdit.exe --self-test test\sample.md self-test.txt
```

The exit code is `0` when every check passes. The report covers: page load, the exact document
round trip, search, formatting commands, undo, and saving to disk.

## Keyboard shortcuts

The editor runs inside a WebView, so the shortcuts are matched in the page and forwarded to the
native host. `Cmd` on macOS maps to `Ctrl` on Windows.

| Action | Shortcut |
| --- | --- |
| New / Open / Save / Save As | `Ctrl+N`, `Ctrl+O`, `Ctrl+S`, `Ctrl+Shift+S` |
| Close / Revert / Quit | `Ctrl+W`, `Ctrl+R`, `Alt+F4` |
| Undo / Redo | `Ctrl+Z`, `Ctrl+Y` |
| Cut / Copy / Paste / Select All | `Ctrl+X`, `Ctrl+C`, `Ctrl+V`, `Ctrl+A` |
| Find / Replace | `Ctrl+F`, `Ctrl+H` |
| Find next / previous | `F3`, `Shift+F3` |
| Use selection for find | `Ctrl+E` |
| Select next / all occurrences | `Ctrl+D`, `Ctrl+Shift+E` |
| Go to line / Table of contents | `Ctrl+G`, `Ctrl+T` |
| Bold / Italic / Strikethrough | `Ctrl+B`, `Ctrl+I`, `Ctrl+Shift+X` |
| Headings 1-6 | `Ctrl+1` … `Ctrl+6` |
| Bullet / numbered list | `Ctrl+Shift+L`, `Ctrl+Shift+O` |
| Insert link | `Ctrl+K` |
| Indent less / more | `Ctrl+[`, `Ctrl+]` |
| Toggle line / block comment | `Ctrl+/`, `Ctrl+Shift+A` |
| Read only / typewriter mode | `Ctrl+Shift+R`, `Ctrl+Shift+D` |
| Font bigger / smaller / reset | `Ctrl+=`, `Ctrl+-`, `Ctrl+0` |

## Customization

Settings live in `Documents\MarkEdit\settings.json`. The shared `editor.*` and `general.*` sections
use the same schema as the macOS app, Windows-only preferences are under `windows.*`:

```json
{
  "editor": {
    "autoCharacterPairs": true,
    "autoSaveWhenIdle": false,
    "indentBehavior": "never"
  },
  "general": {
    "defaultOpenDirectory": "",
    "defaultSaveDirectory": ""
  },
  "windows": {
    "theme": "auto",
    "fontFamily": "Cascadia Mono",
    "fontSize": 17,
    "showLineNumbers": true,
    "showActiveLineIndicator": true,
    "lineWrapping": true,
    "lineHeight": 1.5,
    "invisiblesBehavior": "selection",
    "typewriterMode": false,
    "focusMode": false
  }
}
```

Setting `windows.theme` to `auto` follows the Windows light/dark preference; any editor theme name
such as `github-dark`, `solarized-light` or `dracula` can be used instead. All themes are also
available from *View → Appearance*.

`Documents\MarkEdit` is also where customizations go:

- `editor.css` - extra CSS appended to the editor page
- `editor.js` - a module script appended after the editor bundle
- `scripts\*.js` - additional module scripts, loaded in alphabetical order

User scripts get the full [MarkEdit-api](https://github.com/MarkEdit-app/MarkEdit-api), for example:

```js
MarkEdit.onEditorReady(() => {
  console.log(MarkEdit.editorAPI.getText());
});
```

## What works, and what does not

Implemented in this port:

- The complete CodeMirror 6 editing core: Markdown/GFM highlighting, folding, multi-caret,
  autocompletion, tables, front matter, smart quotes, typewriter and focus modes
- All 16 bundled themes plus automatic light/dark switching
- Open, save, save as, revert, recent files, autosave when idle
- UTF-8, UTF-8 with BOM, UTF-16 LE/BE and ANSI encodings, LF/CRLF/CR line endings
- Find and replace with case, whole word and regular expression options
- Formatting menu, edit commands, table of contents, go to line
- Native context menus, drag and drop, file associations through *Open with*
- The `MarkEdit` scripting API, including `api`, `files`, `pasteboard` and `ui` modules

Not available on Windows, because the macOS implementation relies on Apple frameworks:

- Word completion (`NLP`-backed completion panel), inline predictions and Writing Tools
- Apple Intelligence (`foundationModels` module) and the `translation` module
- File version history (`NSFileVersion`) and macOS Services
- `MarkEdit.runService`, `revealFile` uses Explorer instead of Finder

These report themselves as unavailable, so scripts that use them degrade gracefully instead of
failing.

## Layout

```
windows/
  build.cmd                 build script
  MarkEditWin/
    App.xaml(.cs)           application entry point, WebView2 environment, single instance
    MainWindow.xaml(.cs)    window, editor host, config assembly, web view setup
    MainWindow.Menu.cs      menus, shortcut table, context menus, user menu items
    MainWindow.Commands.cs  command dispatch shared by menus and shortcuts
    MainWindow.Documents.cs file open/save/revert/close, find bar wiring
    Bridge/
      BridgeHost.cs         web <-> native transport
      NativeModuleDispatcher.cs
      BridgeParams.cs
      Modules/              core, api, completion, tokenizer, foundationModels, translation
    Services/               settings, document session, paths, shell, theme, self-test
    Views/                  find bar and modal dialogs
    Assets/bridge-shim.js   preloaded page shim: bridge + shortcut forwarding
```

## License

MIT, like the upstream project. MarkEdit was created by [cyan](https://github.com/cyanzhong) and the
[MarkEdit contributors](https://github.com/MarkEdit-app/MarkEdit/graphs/contributors).
