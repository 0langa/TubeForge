# Third-party notices

## FFmpeg

TubeForge distributes an unmodified FFmpeg command-line executable as a separate
process for MP4, WebM, and MKV stream-copy finalization plus explicitly selected
audio/video conversion and timeline editing. TubeForge does not link to FFmpeg
libraries. Original-quality finalization uses `-c copy`; conversion presets and
SponsorBlock removal re-encode only when the user selects those modes.

- FFmpeg source revision: `94138f6973dd1ac6208ace92148ac0d172455d65`
- FFmpeg version: `8.1.2-22-g94138f6973`
- Windows x64 build variant: `win64-lgpl-8.1`
- Build archive SHA-256:
  `66fdaf7e314968332c4c3fffbe730fedce47f9ac456ae3a04f73cd531080f4b3`
- FFmpeg executable SHA-256:
  `c63b7c29e268acb70f058c2c1863fdeae16830d401b226a6c6d25a29c55a4702`
- Distribution bootstrap archive: TubeForge v1.2.5 framework-dependent ZIP
  (`e1a43566a114a09a71d178608a1a21f1a996121475f1a9681e3b95ea0b639b82`);
  this immutable release archive preserves the exact verified build after upstream
  autobuild retention expires.
- Build scripts revision:
  `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21`

FFmpeg is licensed under LGPL v2.1 or later; optional parts may use other
licenses. TubeForge's pinned build is BtbN's LGPL variant. License text ships as
`ffmpeg/FFmpeg-LICENSE.txt`.

- [FFmpeg project](https://ffmpeg.org/)
- [Exact FFmpeg source](https://github.com/FFmpeg/FFmpeg/archive/94138f6973dd1ac6208ace92148ac0d172455d65.tar.gz)
- [FFmpeg legal information](https://ffmpeg.org/legal.html)
- [Exact BtbN build scripts](https://github.com/BtbN/FFmpeg-Builds/archive/1f74efed63f467dbf0d1e5dd8548bf2188f4ad21.tar.gz)
- [Archived verified binary bootstrap](https://github.com/0langa/TubeForge/releases/download/v1.2.5/TubeForge-1.2.5-win-x64-framework-dependent.zip)

BtbN's FFmpeg-Builds scripts are MIT-licensed. Their license text ships as
`ffmpeg/FFmpeg-Builds-LICENSE.txt`.

TubeForge itself remains licensed under MIT. See `LICENSE`.
