# VLive Live Toon

ライブ制作向けの toon shader / character look を扱う Unity package です。

## Package

- Package name: `com.toshi.vlivekit.livetoon`
- Version: `0.1.5`
- Unity: 2022.3
- Repository: https://github.com/toshi-kundesu/VLiveKit_LiveToon
- Package root: `Assets/toshi.VLiveKit/livetoon`

## 主な内容

- 法線・陰影を整理した toon lighting
- lit / shade 境界の制御
- HDRP 環境で使うキャラクター表現の土台
- VRM/MToon material conversion helpers
- character look controls for face normals, light direction shaping, perspective correction, and custom shadows

## 依存・同梱 asset

- HDRP

## インストール

Unity の `Packages/manifest.json` の `dependencies` に追加します。

```json
{
  "dependencies": {
    "com.toshi.vlivekit.livetoon": "0.1.5"
  }
}
```

VLiveKit sandbox では submodule として `Packages/VLiveKit_LiveToon` に配置し、`file:` 参照で読み込んでいます。

## 注意

- 表現の調整を続けている package なので、material / shader の互換性に注意してください。
- ローカル検証用の root scene は npm package に含めていません。配布用のサンプルシーンと必要な sample character asset は `Samples~` として同梱します。

## License

この package 独自のコードと asset は repository の `LICENSE` に従います。third-party asset を含む場合は、それぞれの license / README を確認してください。
