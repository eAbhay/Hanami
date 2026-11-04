# Third-party notices

Hanami itself is released under the MIT licence (see `LICENSE`). It includes or builds on the following. This file is also
built into `Hanami.exe` and shown in Settings → General → Open-source notices.

Wallpapers and pet packs you import are not part of Hanami and keep their authors' licences.

## Tilt Warp (clock font)

Bundled as `web/wallpaper/fonts/TiltWarp.ttf`, the default screensaver clock font.

Copyright 2019 The Tilt Project Authors (https://github.com/googlefonts/Tilt-Fonts).
Licensed under the SIL Open Font License, Version 1.1. The licence text ships beside the font as
`web/wallpaper/fonts/OFL.txt` and is available with a FAQ at https://openfontlicense.org.

## .NET runtime, Windows Presentation Foundation and Windows Forms

`Hanami.exe` is a self-contained single file, so it carries the .NET runtime and the WPF and Windows Forms libraries.

Copyright (c) .NET Foundation and Contributors. Licensed under the MIT licence:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
> files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy,
> modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
> Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
> Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
> ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The runtime has its own third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

## Microsoft Edge WebView2 SDK (Microsoft.Web.WebView2 1.0.4258.31)

Hanami shows its wallpapers and settings in WebView2, using the SDK's .NET assemblies and loader library, which are part of
`Hanami.exe`. The WebView2 Runtime itself is not included: it comes with Windows 11.

Copyright (C) Microsoft Corporation. All rights reserved. The SDK's licence, reproduced from the package:

> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following
> conditions are met:
>
> * Redistributions of source code must retain the above copyright notice, this list of conditions and the following
>   disclaimer.
> * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
>   disclaimer in the documentation and/or other materials provided with the distribution.
> * The name of Microsoft Corporation, or the names of its contributors may not be used to endorse or promote products derived
>   from this software without specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING,
> BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO
> EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
> CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
> PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
> TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
> POSSIBILITY OF SUCH DAMAGE.

## Shimeji-ee (desktop pet frame timings)

Hanami reads Shimeji image sets (`shime1.png` … `shime46.png`) for its desktop pets. It contains no Shimeji-ee code, but the
default frame timings in `Core/Pets/ShimejiFrames.cs` follow the `conf/actions.xml` of Shimeji-ee, so that existing packs
animate the way their authors expect. The original Shimeji is by Yuki Yamada (2009); Shimeji-ee continues it, and its
maintainer describes its licence as a permissive one ("New BSD"). No licence text is reproduced here because none of its code
is included. Thank you to those projects and to the authors of the packs people use with them.
