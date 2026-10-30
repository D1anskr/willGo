# Large Installer Module

`7zSD.sfx` is the unmodified installer module from LZMA SDK 26.04, published
in the public domain by Igor Pavlov. Source and SDK documentation:
https://github.com/ip7z/7zip/releases/tag/26.04

SDK archive `lzma2604.7z` SHA256:
bd98058d0e12c5da970dd4e5e1d8fa24f2d704914d96e69c28f92ed45421a439

Module `bin/7zSD.sfx` SHA256:
837a2d84a4d15366af7a91670908aa29a958b721f40db46da734621f6234fea2

Large installers append a small UTF-8 installer configuration and an uncompressed
7z archive containing the already-compressed distribution ZIP and bootstrap.
This avoids IExpress cabinet limits and recompressing a gigabyte of compressed
model data. Runtime extraction and installation require only standard Windows
PowerShell. 7-Zip is needed on the build machine only.
