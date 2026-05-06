# VLive Live Toon

ライブ制作向けの toon shader / character look を扱う Unity package です。

## Package

- Package name: `com.toshi.vlivekit.livetoon`
- Version: `0.0.8`
- Unity: 2022.3
- Repository: https://github.com/toshi-kundesu/VLiveKit_LiveToon
- Package root: `Assets/toshi.VLiveKit/livetoon`

## 主な内容

- 法線・陰影を整理した toon lighting
- lit / shade 境界の制御
- HDRP 環境で使うキャラクター表現の土台

## 依存・同梱 asset

- HDRP 14.0.8

## インストール

Unity の `Packages/manifest.json` の `dependencies` に追加します。

```json
{
  "dependencies": {
    "com.toshi.vlivekit.livetoon": "https://github.com/toshi-kundesu/VLiveKit_LiveToon.git?path=/Assets/toshi.VLiveKit/livetoon#main"
  }
}
```

VLiveKit sandbox では submodule として `Packages/VLiveKit_LiveToon` に配置し、`file:` 参照で読み込んでいます。

## 注意

- 表現の調整を続けている package なので、material / shader の互換性に注意してください。

## License

この package 独自のコードと asset は repository の `LICENSE` に従います。third-party asset を含む場合は、それぞれの license / README を確認してください。
