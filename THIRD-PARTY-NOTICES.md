# Third-party notices

Pixel itself is MIT-licensed (see [LICENSE](LICENSE)). It includes or downloads the following, each under its own
license.

## Bundled in this repository (chat panel, `src/Pixel.App/web/vendor/`)

### marked — MIT
Markdown rendering. https://github.com/markedjs/marked

Copyright (c) 2018+, MarkedJS (https://github.com/markedjs/)
Copyright (c) 2011-2018, Christopher Jeffrey (https://github.com/chjj/)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### DOMPurify — Apache-2.0 or MPL-2.0
HTML sanitising. https://github.com/cure53/DOMPurify — (c) Cure53 and other contributors. Used under Apache-2.0
(https://www.apache.org/licenses/LICENSE-2.0); the license header is kept in `purify.min.js`.

### highlight.js — BSD-3-Clause
Code highlighting (v11.11.2). https://github.com/highlightjs/highlight.js — (c) 2006-2026 Josh Goebel and other
contributors. The license header is kept in `highlight.min.js`.

## Shipped in the release build

### llama.cpp — MIT
Runs the model (`runtime\llama\…`, build b11476). https://github.com/ggml-org/llama.cpp — Copyright (c) 2023-2026
The ggml authors. Full text: [licenses/llama.cpp-LICENSE.txt](licenses/llama.cpp-LICENSE.txt). Its CPU build includes
the LLVM OpenMP runtime; that license ships as `runtime\llama\cpu\LICENSE-LLVM-OpenMP`.

### .NET runtime, WPF and Windows Forms — MIT
Copyright (c) .NET Foundation and Contributors. https://github.com/dotnet/runtime, https://github.com/dotnet/wpf,
https://github.com/dotnet/winforms

### Microsoft.Data.Sqlite — MIT; SQLitePCLRaw — Apache-2.0; SQLite — public domain
Chat history storage. https://github.com/dotnet/efcore, https://github.com/ericsink/SQLitePCL.raw, https://sqlite.org

### Microsoft WebView2 SDK — BSD-style license
Hosts the chat panel. Copyright (C) Microsoft Corporation. https://www.nuget.org/packages/Microsoft.Web.WebView2

## Downloaded on first use (not shipped)

### Qwen3-VL-2B-Instruct (GGUF) — Apache-2.0
The default brain, from https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct-GGUF. Copyright Alibaba Cloud (Qwen team).
