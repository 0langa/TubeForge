# Third-party notices

## FFmpeg

TubeForge distributes an unmodified FFmpeg command-line executable as a separate
process for MP4, WebM, and MKV stream-copy muxing and compatibility normalization. TubeForge
does not link to FFmpeg libraries. Media finalization uses `-c copy`, so video and
audio are not re-encoded and source quality is preserved.

- FFmpeg source revision: `94138f6973dd1ac6208ace92148ac0d172455d65`
- FFmpeg version: `8.1.2-22-g94138f6973`
- Windows x64 build variant: `win64-lgpl-8.1`
- Build archive SHA-256:
  `66fdaf7e314968332c4c3fffbe730fedce47f9ac456ae3a04f73cd531080f4b3`
- Build scripts revision:
  `1f74efed63f467dbf0d1e5dd8548bf2188f4ad21`

FFmpeg is licensed under LGPL v2.1 or later; optional parts may use other
licenses. TubeForge's pinned build is BtbN's LGPL variant. License text ships as
`ffmpeg/FFmpeg-LICENSE.txt`.

- [FFmpeg project](https://ffmpeg.org/)
- [Exact FFmpeg source](https://github.com/FFmpeg/FFmpeg/archive/94138f6973dd1ac6208ace92148ac0d172455d65.tar.gz)
- [FFmpeg legal information](https://ffmpeg.org/legal.html)
- [Exact BtbN build scripts](https://github.com/BtbN/FFmpeg-Builds/archive/1f74efed63f467dbf0d1e5dd8548bf2188f4ad21.tar.gz)
- [Pinned build release](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-07-17-13-22)

BtbN's FFmpeg-Builds scripts are MIT-licensed. Their license text ships as
`ffmpeg/FFmpeg-Builds-LICENSE.txt`.

TubeForge itself remains licensed under MIT. See `LICENSE`.
