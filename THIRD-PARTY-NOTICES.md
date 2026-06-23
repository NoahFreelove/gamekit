# Third-Party Notices

This file lists third-party software incorporated into GameKit, along with their licenses.

---

## MaartenStaa/glicko2-csharp

**Purpose:** In-house vendored port of the Glicko-2 rating algorithm. The four source files under
`src/GameKit.Rankings/Glicko2/` (`Rating.cs`, `RatingCalculator.cs`, `RatingPeriodResults.cs`,
`Result.cs`) are derived from this upstream reference implementation.

**Upstream URL:** https://github.com/MaartenStaa/glicko2-csharp

**Upstream commit at time of vendoring:** `59033eeca27a49a444897430dc0a63a33bc99870`

**SPDX-License-Identifier:** `BSD-3-Clause`

**Note:** CLAUDE.md and `04-CONTEXT.md` both incorrectly describe this dependency as "MIT".
The actual license, read verbatim from the upstream repository at the commit above, is
BSD-3-Clause (three-clause BSD). The non-endorsement clause (third bullet) is present:
"Neither the name of glicko2-csharp nor the names of its contributors may be used to endorse
or promote products derived from this software without specific prior written permission."

**Per-file vendored-source header (for use in all `src/GameKit.Rankings/Glicko2/*.cs` files):**

```csharp
// SPDX-License-Identifier: BSD-3-Clause AND GPL-3.0-or-later
// Original work Copyright (c) 2015, Maarten Staa (BSD-3-Clause)
// https://github.com/MaartenStaa/glicko2-csharp commit 59033eec
// Modified work Copyright (c) 2026 GameKit contributors (GPL-3.0-or-later)
```

---

## three.js

**Purpose:** WebGL 3D engine powering the Platformer3D browser client.
Bundled locally (no CDN) at `samples/Platformer3D/wwwroot/js/three.module.js`,
`samples/Platformer3D/wwwroot/js/three.core.js`, and
`samples/Platformer3D/wwwroot/js/addons/PointerLockControls.js`.

**Upstream URL:** https://github.com/mrdoob/three.js

**Version vendored:** r184

**SPDX-License-Identifier:** `MIT`

**Full verbatim LICENSE text (from upstream tag `r184`):**

```
The MIT License

Copyright © 2010-2026 three.js authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

---

**Full verbatim LICENSE text (from upstream commit `59033eec`):**

```
Copyright (c) 2015, Maarten Staa
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of glicko2-csharp nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
