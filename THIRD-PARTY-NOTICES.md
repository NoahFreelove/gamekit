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
