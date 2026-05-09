## 概要

VLiveKitの一部として開発している、  
ライブ制作向けキャラクターシェーダーです。

実際のライブ制作で使用しながら改善しているもので、  
現在は整理およびリファクタリングを進めています。

---

## 実装済み機能（整理中）

- 法線の球面化によるライティングの安定化
- lit / shade 境界の彩度制御による表面下散乱風表現
- 視野角依存の見え方を補正するジオメトリシェーディング
- 前髪の落ち影を整えるためのカスタムライト
- HDRP対応（HDRP向けToonシェーダーとして動作）

---

## 特徴

- 複数ライト環境でもルックが崩れにくい設計
- ライブ用途を前提とした安定した描画

他のポストプロセスと組み合わせることで表現の幅が広がります：

- Diffusion
- キャラクターブルーム
- 原神ライクなポストプロセス

（※ これらは別パッケージに含まれます）

---

## 今後の予定

- 汗などのライブ向け表現の追加
- キャラクター表現の拡張
- 全体的なリファクタリング

---

## インストール

`Packages/manifest.json` の `dependencies` に以下を追加してください。

```json
{
  "dependencies": {
    "com.toshi.vlivekit.livetoon": "https://github.com/toshi-kundesu/VLiveKit_livetoon.git?path=/Assets/toshi.VLiveKit/livetoon#main"
  }
}
```

---

## LiveToon Shader Converter

Open the converter from `toshi/VLiveKit/LiveToon/Shader Converter`.

The converter currently uses a LoadModel-style baseline for VRM 0.x / MToon materials:

- swaps the material shader to `toshi/VLiveKit/livetoon`
- preserves existing MToon-compatible material properties such as `_CullMode`, `_BlendMode`, `_SrcBlend`, `_DstBlend`, and `_ZWrite`
- preserves `_Color`, including alpha, so MToon Transparent opacity is carried into LiveToon
- fills `_ShadeTexture` from `_MainTex` only when `_ShadeTexture` is empty
- updates `renderQueue` from `_BlendMode`: Opaque `2225`, Cutout `2450`, Transparent `3000`
- restores the material `RenderType` tag and alpha keywords from `_BlendMode` after the shader swap
- restores LiveToon's Forward pass ZTest from `_BlendMode`: Opaque uses `Equal`, Cutout/Transparent use `LEqual`
- enables HDRP transparent fog for Transparent materials while keeping the shader-side fog path guarded by `_ENABLE_FOG_ON_TRANSPARENT`
- keeps VRM Transparent materials as Transparent and uses LiveToon's legacy transparent opacity formula based on `_TransparentThreshold`, `_MainTex` alpha/red, and `_Color.a`
- disables outline by default during conversion while LiveToon outline rendering is being tuned
- optionally creates material backup assets beside the source material before converting
- restores selected model materials from those backups with `Restore Materials From Backups`

Backups are stored in a `LiveToonMaterialBackups` folder next to the source material asset. The converter keeps the first backup instead of overwriting it, so you can compare and restore the original VRM 0.x material values while debugging culling and double-sided rendering.
